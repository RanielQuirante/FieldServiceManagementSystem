namespace FieldService.Mobile.Shared.Controls;

public static class LineHeights
{
    public const double FontNaturalLineHeight = 1.30;

    public static readonly BindableProperty SpecProperty = BindableProperty.CreateAttached(
        "Spec",
        typeof(double),
        typeof(LineHeights),
        0d);

    public static double GetSpec(BindableObject view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return (double)view.GetValue(SpecProperty);
    }

    public static void SetSpec(BindableObject view, double value)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.SetValue(SpecProperty, value);
    }

    public static double SpecFor(double themeLineHeight) => Math.Round(themeLineHeight, 2) switch
    {
        1.05 => 1.35,
        1.1 => 1.425,
        1.15 => 1.5,
        _ => themeLineHeight * FontNaturalLineHeight,
    };

    public static double SpecFor(double themeLineHeight, double spec) =>
        themeLineHeight > 0 && spec > 0 ? spec : SpecFor(themeLineHeight);

    public static double WindowsLineHeight(double themeLineHeight, double naturalLineHeight) =>
        WindowsLineHeight(themeLineHeight, 0, naturalLineHeight);

    public static double WindowsLineHeight(double themeLineHeight, double spec, double naturalLineHeight) =>
        themeLineHeight > 0 && naturalLineHeight > 0 ? SpecFor(themeLineHeight, spec) / FontNaturalLineHeight * naturalLineHeight : 0;
}
