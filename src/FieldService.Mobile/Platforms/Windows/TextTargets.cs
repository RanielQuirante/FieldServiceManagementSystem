using FieldService.Mobile.Shared.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FieldService.Mobile.WinUI;

public static class TextTargets
{
    public static void PrepareForFocus(TextBlock text, BindableObject? label)
    {
        ArgumentNullException.ThrowIfNull(text);

        var key = label is not null && FocusRing.GetIsOnDark(label) ? FocusRing.DarkColorKey : FocusRing.LightColorKey;
        FocusVisuals.Apply(text, key, FocusVisuals.ControlMargin);
        if (text.IsTabStop)
        {
            return;
        }

        text.IsTabStop = true;
        text.LostFocus -= OnLostFocus;
        text.LostFocus += OnLostFocus;
    }

    private static void OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBlock text)
        {
            text.LostFocus -= OnLostFocus;
            text.IsTabStop = false;
        }
    }
}
