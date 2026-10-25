namespace FieldService.Mobile.Shared.Controls;

public static class ListSemantics
{
    public static readonly BindableProperty IsOwnStopProperty = BindableProperty.CreateAttached(
        "IsOwnStop",
        typeof(bool),
        typeof(ListSemantics),
        false);

    public static bool GetIsOwnStop(BindableObject view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return (bool)view.GetValue(IsOwnStopProperty);
    }

    public static void SetIsOwnStop(BindableObject view, bool value)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.SetValue(IsOwnStopProperty, value);
    }
}
