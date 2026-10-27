using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace FieldService.Mobile.WinUI;

public static class KeyboardFocus
{
    private static readonly PointerEventHandler _keepFocus = (_, e) => e.Handled = true;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, object> _holdsHiddenFocus = new();
    private static bool _watchingHiddenFocus;

    public static void HoldHiddenFocus(FrameworkElement container, bool hold)
    {
        ArgumentNullException.ThrowIfNull(container);

        if (!hold)
        {
            _holdsHiddenFocus.Remove(container);
            return;
        }

        _holdsHiddenFocus.AddOrUpdate(container, new object());
        if (!_watchingHiddenFocus)
        {
            _watchingHiddenFocus = true;
            FocusManager.GettingFocus += OnGettingFocusFromHidden;
        }
    }

    private static void OnGettingFocusFromHidden(object? sender, GettingFocusEventArgs args)
    {
        if (args.OldFocusedElement is not UIElement old || HeldContainer(old) is not { } held)
        {
            return;
        }

        var newHeld = args.NewFocusedElement is DependencyObject next && IsInside(next, held);
        if (!FieldService.Mobile.Shared.Controls.FocusMoves.HoldsRecovery(
                args.Direction == FocusNavigationDirection.None,
                FieldService.Mobile.Shared.Controls.FocusMoves.IsMoving,
                IsShown(old),
                oldHeld: true,
                newHeld)
            || !args.TryCancel())
        {
            return;
        }

        var timer = old.DispatcherQueue.CreateTimer();
        timer.Interval = FieldService.Mobile.Shared.Controls.FocusMoves.HiddenFocusGrace;
        timer.IsRepeating = false;
        timer.Tick += (tick, _) =>
        {
            tick.Stop();
            if (old.XamlRoot is not { } root || !ReferenceEquals(FocusManager.GetFocusedElement(root), old))
            {
                return;
            }

            if (FocusManager.FindFirstFocusableElement(held) is Microsoft.UI.Xaml.Controls.Control first && IsShown(first))
            {
                first.Focus(FocusState.Programmatic);
                return;
            }

            if (root.Content is { } content)
            {
                FocusManager.TryMoveFocus(FocusNavigationDirection.Next, new FindNextElementOptions { SearchRoot = content });
            }
        };
        timer.Start();
    }

    private static FrameworkElement? HeldContainer(DependencyObject element)
    {
        for (var node = VisualTreeHelper.GetParent(element); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement container && _holdsHiddenFocus.TryGetValue(container, out _))
            {
                return container;
            }
        }

        return null;
    }

    private static bool IsInside(DependencyObject element, DependencyObject container)
    {
        for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, container))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsShown(DependencyObject element)
    {
        for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is UIElement { Visibility: Microsoft.UI.Xaml.Visibility.Collapsed })
            {
                return false;
            }
        }

        return true;
    }

    public static void KeepPointerFocus(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.IsTabStop = false;
        element.AllowFocusOnInteraction = false;
        element.RemoveHandler(UIElement.PointerPressedEvent, _keepFocus);
        element.AddHandler(UIElement.PointerPressedEvent, _keepFocus, false);
    }

    public static void RevealFocusedWithin(Microsoft.UI.Xaml.Controls.ScrollViewer scroller)
    {
        ArgumentNullException.ThrowIfNull(scroller);

        if (IsWithin(scroller) != true
            || scroller.XamlRoot is not { } root
            || FocusManager.GetFocusedElement(root) is not FrameworkElement { ActualHeight: > 0 } focused
            || scroller.Content is not UIElement content)
        {
            return;
        }

        var top = focused.TransformToVisual(content).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
        if (FieldService.Mobile.Shared.Controls.ScrollRequests.RevealOffset(top, focused.ActualHeight, scroller.VerticalOffset, scroller.ViewportHeight, 0) is { } offset)
        {
            scroller.ChangeView(null, offset, null, true);
        }
    }

    public static bool? IsWithin(FrameworkElement scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (scope.XamlRoot is not { } root)
        {
            return null;
        }

        if (FocusManager.GetFocusedElement(root) is not DependencyObject focused)
        {
            return false;
        }

        for (DependencyObject? node = focused; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is UIElement { Visibility: Microsoft.UI.Xaml.Visibility.Collapsed })
            {
                return false;
            }

            if (ReferenceEquals(node, scope))
            {
                return true;
            }
        }

        return false;
    }
}
