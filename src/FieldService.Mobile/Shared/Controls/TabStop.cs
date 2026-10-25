namespace FieldService.Mobile.Shared.Controls;

public static class TabStop
{
    public static readonly BindableProperty IsStopProperty = BindableProperty.CreateAttached(
        "IsStop",
        typeof(bool),
        typeof(TabStop),
        true);

    public static bool GetIsStop(BindableObject view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return (bool)view.GetValue(IsStopProperty);
    }

    public static void SetIsStop(BindableObject view, bool value)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.SetValue(IsStopProperty, value);
    }
}
