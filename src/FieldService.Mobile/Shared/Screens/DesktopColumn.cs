namespace FieldService.Mobile.Shared.Screens;

public static class DesktopColumn
{
    public const double Width = 720;

    public static double MaxWidth(IWindowLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return layout.IsTwoPane ? Width : double.PositiveInfinity;
    }

    public static LayoutOptions Options(IWindowLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return layout.IsTwoPane ? LayoutOptions.Center : LayoutOptions.Fill;
    }
}
