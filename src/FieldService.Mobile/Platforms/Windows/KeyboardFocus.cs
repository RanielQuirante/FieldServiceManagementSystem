using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace FieldService.Mobile.WinUI;

public static class KeyboardFocus
{
    private static readonly PointerEventHandler _keepFocus = (_, e) => e.Handled = true;

    public static void KeepPointerFocus(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.IsTabStop = false;
        element.AllowFocusOnInteraction = false;
        element.RemoveHandler(UIElement.PointerPressedEvent, _keepFocus);
        element.AddHandler(UIElement.PointerPressedEvent, _keepFocus, false);
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
