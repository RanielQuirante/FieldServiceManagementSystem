using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;
using PlatformAutomationProperties = Microsoft.UI.Xaml.Automation.AutomationProperties;

namespace FieldService.Mobile.WinUI;

public static class CoveredSubtrees
{
    private static readonly ConditionalWeakTable<UIElement, Saved> _saved = new();
    private static readonly ConditionalWeakTable<FrameworkElement, Root> _roots = new();

    public static void Apply(FrameworkElement root, bool excluded)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (!_roots.TryGetValue(root, out var state))
        {
            if (!excluded)
            {
                return;
            }

            state = new Root();
            _roots.Add(root, state);
            root.Loaded += OnRootLoaded;
        }

        state.IsExcluded = excluded;
        if (!excluded)
        {
            Show(state);
        }
        else if (root.IsLoaded)
        {
            Hide(root, state);
        }
    }

    public static bool IsCovered(UIElement element) =>
        element is not null && _saved.TryGetValue(element, out var saved) && saved.Count > 0;

    private static void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement root && _roots.TryGetValue(root, out var state) && state.IsExcluded)
        {
            Hide(root, state);
        }
    }

    private static void Hide(FrameworkElement root, Root state)
    {
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node is UIElement element && state.Hidden.Add(element))
            {
                Cover(element);
            }

            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var index = 0; index < count; index++)
            {
                pending.Push(VisualTreeHelper.GetChild(node, index));
            }
        }
    }

    private static void Show(Root state)
    {
        foreach (var element in state.Hidden)
        {
            Uncover(element);
        }

        state.Hidden.Clear();
    }

    private static void Cover(UIElement element)
    {
        var saved = _saved.GetOrCreateValue(element);
        saved.Count++;
        if (saved.Count > 1)
        {
            return;
        }

        saved.View = element.ReadLocalValue(PlatformAutomationProperties.AccessibilityViewProperty);
        saved.TabStop = element.ReadLocalValue(UIElement.IsTabStopProperty);
        element.SetValue(PlatformAutomationProperties.AccessibilityViewProperty, AccessibilityView.Raw);
        if (element.IsTabStop)
        {
            element.IsTabStop = false;
        }
    }

    private static void Uncover(UIElement element)
    {
        if (!_saved.TryGetValue(element, out var saved) || saved.Count == 0)
        {
            return;
        }

        saved.Count--;
        if (saved.Count > 0)
        {
            return;
        }

        Restore(element, PlatformAutomationProperties.AccessibilityViewProperty, saved.View);
        Restore(element, UIElement.IsTabStopProperty, saved.TabStop);
    }

    private static void Restore(UIElement element, DependencyProperty property, object value)
    {
        if (value == DependencyProperty.UnsetValue)
        {
            element.ClearValue(property);
        }
        else
        {
            element.SetValue(property, value);
        }
    }

    private sealed class Saved
    {
        public int Count { get; set; }

        public object View { get; set; } = DependencyProperty.UnsetValue;

        public object TabStop { get; set; } = DependencyProperty.UnsetValue;
    }

    private sealed class Root
    {
        public bool IsExcluded { get; set; }

        public HashSet<UIElement> Hidden { get; } = [];
    }
}
