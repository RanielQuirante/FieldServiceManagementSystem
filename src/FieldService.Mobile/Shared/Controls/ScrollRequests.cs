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

        void Reveal()
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

        void OnResized(object? sender, EventArgs e)
        {
            element.SizeChanged -= OnResized;
            element.Dispatcher.Dispatch(Reveal);
        }

        element.SizeChanged += OnResized;
        element.Dispatcher.Dispatch(Reveal);
        element.Dispatcher.DispatchDelayed(RevealWindow, () => element.SizeChanged -= OnResized);
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

        element.Dispatcher.Dispatch(() => element.SetSemanticFocus());
    }
}
