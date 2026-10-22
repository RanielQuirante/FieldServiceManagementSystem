namespace FieldService.Mobile.Shared.Controls;

public static class ScrollRequests
{
    public static readonly BindableProperty ToTopProperty = BindableProperty.CreateAttached(
        "ToTop",
        typeof(int),
        typeof(ScrollRequests),
        0,
        propertyChanged: OnToTopChanged);

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
}
