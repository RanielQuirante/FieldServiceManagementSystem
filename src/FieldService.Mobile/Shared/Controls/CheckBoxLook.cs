namespace FieldService.Mobile.Shared.Controls;

public static class CheckBoxLook
{
    public const double BoxSize = 20;
    public const double BorderWidth = 1.5;
    public const string FillKey = "FsSurface";

    public static double? InsetFor(double width, double height) =>
        width > 0 && height > 0 && !double.IsInfinity(width) && !double.IsInfinity(height)
            ? Math.Max(0, (Math.Min(width, height) - BoxSize) / 2)
            : null;
}
