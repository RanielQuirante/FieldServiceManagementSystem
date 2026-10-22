using Microsoft.Maui.Handlers;

namespace FieldService.Mobile.WinUI;

public static class WindowsPlatformSetup
{
    public const string FieldChromeMappingKey = "FsFieldChrome";

    private const string FocusedBorderThicknessKey = "TextControlBorderThemeThicknessFocused";

    public static void Configure(MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        EntryHandler.Mapper.AppendToMapping(FieldChromeMappingKey, (handler, _) =>
        {
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            handler.PlatformView.Resources[FocusedBorderThicknessKey] = new Microsoft.UI.Xaml.Thickness(0);
        });
        EditorHandler.Mapper.AppendToMapping(FieldChromeMappingKey, (handler, _) =>
        {
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            handler.PlatformView.Resources[FocusedBorderThicknessKey] = new Microsoft.UI.Xaml.Thickness(0);
        });
    }
}
