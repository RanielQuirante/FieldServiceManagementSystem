namespace FieldService.Mobile.Shared.Controls;

public static class KeyboardState
{
    public static readonly BindableProperty ResizesPageProperty = BindableProperty.CreateAttached(
        "ResizesPage",
        typeof(bool),
        typeof(KeyboardState),
        false);

    public static readonly BindableProperty IsOpenProperty = BindableProperty.CreateAttached(
        "IsOpen",
        typeof(bool),
        typeof(KeyboardState),
        false,
        BindingMode.OneWayToSource);

    public static bool GetResizesPage(BindableObject view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return (bool)view.GetValue(ResizesPageProperty);
    }

    public static void SetResizesPage(BindableObject view, bool value)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.SetValue(ResizesPageProperty, value);
    }

    public static bool GetIsOpen(BindableObject view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return (bool)view.GetValue(IsOpenProperty);
    }

    public static void SetIsOpen(BindableObject view, bool value)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.SetValue(IsOpenProperty, value);
    }

    public static bool UsesResize(bool sheetOpen, bool pageResizes) => sheetOpen || pageResizes;
}
