namespace FieldService.Mobile.Shared.Controls;

public static class ScrollRequests
{
    public const double RevealClearance = 16;

    public static readonly TimeSpan RevealWindow = TimeSpan.FromMilliseconds(500);

    public static readonly BindableProperty ToTopProperty = BindableProperty.CreateAttached(
        "ToTop",
        typeof(int),
        typeof(ScrollRequests),
        0,
        propertyChanged: OnToTopChanged);

    public static readonly BindableProperty IntoViewProperty = BindableProperty.CreateAttached(
        "IntoView",
        typeof(int),
        typeof(ScrollRequests),
        0,
        propertyChanged: OnIntoViewChanged);

    public const string RevealFocusedCommand = "FsRevealFocused";

    public static readonly BindableProperty FocusedIntoViewProperty = BindableProperty.CreateAttached(
        "FocusedIntoView",
        typeof(int),
        typeof(ScrollRequests),
        0,
        propertyChanged: OnFocusedIntoViewChanged);

    public static int GetFocusedIntoView(BindableObject view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return (int)view.GetValue(FocusedIntoViewProperty);
    }

    public static void SetFocusedIntoView(BindableObject view, int value)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.SetValue(FocusedIntoViewProperty, value);
    }

    private static void OnFocusedIntoViewChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not ScrollView scroll || (int)newValue == 0 || Equals(oldValue, newValue) || scroll.Handler is null)
        {
            return;
        }

        _focusWatches.GetValue(scroll, watched => new FocusedViewWatch(watched)).Request();
    }

    public static bool RevealsAfterResize(double oldHeight, double newHeight, long sinceRequest) =>
        newHeight < oldHeight - 0.5 || (sinceRequest >= 0 && sinceRequest < FocusedRevealWindow.TotalMilliseconds);

    public static readonly TimeSpan FocusedRevealWindow = TimeSpan.FromMilliseconds(1500);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScrollView, FocusedViewWatch> _focusWatches = new();

    private static void RevealFocused(ScrollView scroll)
    {
        if (FocusedUnit(scroll) is { } unit)
        {
            RevealInScroll(unit);
            return;
        }

        scroll.Handler?.Invoke(RevealFocusedCommand);
    }

    private sealed class FocusedViewWatch
    {
        private readonly ScrollView _scroll;
        private double _height;
        private long _requestedAt = -1;

        public FocusedViewWatch(ScrollView scroll)
        {
            _scroll = scroll;
            _height = scroll.Height;
            scroll.SizeChanged += OnSizeChanged;
        }

        public void Request()
        {
            _requestedAt = Environment.TickCount64;
            _scroll.Dispatcher.Dispatch(() => RevealFocused(_scroll));
            _scroll.Dispatcher.DispatchDelayed(RevealWindow, () => RevealFocused(_scroll));
        }

        private void OnSizeChanged(object? sender, EventArgs e)
        {
            var reveal = RevealsAfterResize(_height, _scroll.Height, _requestedAt < 0 ? -1 : Environment.TickCount64 - _requestedAt);
            _height = _scroll.Height;
            if (reveal)
            {
                _scroll.Dispatcher.Dispatch(() => RevealFocused(_scroll));
            }
        }
    }

    public static readonly BindableProperty SemanticFocusProperty = BindableProperty.CreateAttached(
        "SemanticFocus",
        typeof(int),
        typeof(ScrollRequests),
        0,
        propertyChanged: OnSemanticFocusChanged);

    public static int GetIntoView(BindableObject view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return (int)view.GetValue(IntoViewProperty);
    }

    public static void SetIntoView(BindableObject view, int value)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.SetValue(IntoViewProperty, value);
    }

    public static int GetSemanticFocus(BindableObject view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return (int)view.GetValue(SemanticFocusProperty);
    }

    public static void SetSemanticFocus(BindableObject view, int value)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.SetValue(SemanticFocusProperty, value);
    }

    public static int GetToTop(BindableObject view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return (int)view.GetValue(ToTopProperty);
    }

    public static void SetToTop(BindableObject view, int value)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.SetValue(ToTopProperty, value);
    }

    private static void OnToTopChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not ScrollView scroll || (int)newValue == 0 || Equals(oldValue, newValue) || scroll.Handler is null)
        {
            return;
        }

        scroll.Dispatcher.Dispatch(() => _ = scroll.ScrollToAsync(0, 0, true));
    }

    private static void OnIntoViewChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not VisualElement element || (int)newValue == 0 || Equals(oldValue, newValue) || element.Handler is null)
        {
            return;
        }

        void Reveal() => RevealInScroll(element);

        void OnResized(object? sender, EventArgs e)
        {
            element.SizeChanged -= OnResized;
            element.Dispatcher.Dispatch(Reveal);
        }

        element.SizeChanged += OnResized;
        element.Dispatcher.Dispatch(Reveal);
        element.Dispatcher.DispatchDelayed(RevealWindow, () => element.SizeChanged -= OnResized);
    }

    public static VisualElement? FocusedUnit(Element scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var focused = scope.GetVisualTreeDescendants().OfType<VisualElement>().LastOrDefault(element => element.IsFocused && FocusMoves.IsShown(element));
        for (Element? node = focused; node is not null && !ReferenceEquals(node, scope); node = node.Parent)
        {
            if (node is FormField or CheckboxRow or OptionRow)
            {
                return (VisualElement)node;
            }
        }

        return focused;
    }

    private static void RevealInScroll(VisualElement element)
    {
        Element? ancestor = element.Parent;
        var top = element.Y;
        while (ancestor is not null and not ScrollView)
        {
            if (ancestor is VisualElement visual)
            {
                top += visual.Y;
            }

            ancestor = ancestor.Parent;
        }

        if (ancestor is not ScrollView scroll || scroll.Handler is null)
        {
            return;
        }

        var inset = (scroll.Parent as Layout)?.Children.OfType<StatusBarProtection>().FirstOrDefault()?.Inset ?? 0;
        if (RevealOffset(top, element.Height, scroll.ScrollY, scroll.Height, inset) is { } offset)
        {
            _ = scroll.ScrollToAsync(0, offset, false);
        }
    }

    public static double? RevealOffset(double top, double height, double scrollY, double viewportHeight, double topInset)
    {
        var bottom = top + height + RevealClearance;
        var start = top - RevealClearance - topInset;
        if (start < scrollY || bottom - start > viewportHeight)
        {
            return Math.Max(0, start);
        }

        return bottom > scrollY + viewportHeight ? bottom - viewportHeight : null;
    }

    private static void OnSemanticFocusChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not VisualElement element || (int)newValue == 0 || Equals(oldValue, newValue) || element.Handler is null)
        {
            return;
        }

        element.Dispatcher.Dispatch(() => FocusMoves.MoveTo(element));
    }
}
