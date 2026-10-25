using FieldService.Mobile.Shared.Controls;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PlatformBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using PlatformCornerRadius = Microsoft.UI.Xaml.CornerRadius;
using PlatformThickness = Microsoft.UI.Xaml.Thickness;

namespace FieldService.Mobile.WinUI;

public static class FocusVisuals
{
    private static readonly Color _lightFallback = Color.FromArgb("#0F766E");
    private static readonly Color _darkFallback = Color.FromArgb("#FCD34D");

    public static Color DarkFocus => ColorFor(FocusRing.DarkColorKey);

    public static Color ColorFor(string key) =>
        Microsoft.Maui.Controls.Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : key == FocusRing.DarkColorKey ? _darkFallback : _lightFallback;

    public static PlatformThickness ControlMargin => new(FocusRing.ControlMargin);

    public static PlatformThickness MarginAround(Microsoft.Maui.Thickness inset)
    {
        var margin = FocusRing.MarginAround(inset);
        return new PlatformThickness(margin.Left, margin.Top, margin.Right, margin.Bottom);
    }

    public static void Apply(FrameworkElement element, string colorKey, PlatformThickness margin)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.UseSystemFocusVisuals = true;
        element.FocusVisualPrimaryBrush = new PlatformBrush(ColorFor(colorKey).ToWindowsColor());
        element.FocusVisualPrimaryThickness = new PlatformThickness(FocusRing.Thickness);
        element.FocusVisualSecondaryThickness = new PlatformThickness(0);
        element.FocusVisualSecondaryBrush = new PlatformBrush(Microsoft.UI.Colors.Transparent);
        element.FocusVisualMargin = margin;
    }

    public static void Round(Control control, double radius)
    {
        ArgumentNullException.ThrowIfNull(control);

        var corner = FocusRing.CornerRadiusAround(radius);
        control.CornerRadius = new PlatformCornerRadius(corner);
    }
}
