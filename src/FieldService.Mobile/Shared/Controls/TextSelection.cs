namespace FieldService.Mobile.Shared.Controls;

public static class TextSelection
{
    public const string MappingKey = "FsTextSelection";

    public static readonly BindableProperty IsEnabledProperty = BindableProperty.CreateAttached(
        "IsEnabled",
        typeof(bool),
        typeof(TextSelection),
        false,
        propertyChanged: (bindable, _, _) => (bindable as Element)?.Handler?.UpdateValue(MappingKey));

    public static bool GetIsEnabled(BindableObject view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return (bool)view.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(BindableObject view, bool value)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.SetValue(IsEnabledProperty, value);
    }
}
