using FieldService.Mobile.Shared.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PlatformBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using PlatformCheckBox = Microsoft.UI.Xaml.Controls.CheckBox;
using PlatformThickness = Microsoft.UI.Xaml.Thickness;

namespace FieldService.Mobile.WinUI;

public static class WindowsPlatformSetup
{
    public const string FieldChromeMappingKey = "FsFieldChrome";
    public const string CheckBoxLookMappingKey = "FsCheckBoxLook";
    public const string CheckBoxBoxPartName = "NormalRectangle";

    private const string FocusedBorderThicknessKey = "TextControlBorderThemeThicknessFocused";

    private static readonly string[] _uncheckedFillKeys =
    [
        "CheckBoxCheckBackgroundFillUnchecked",
        "CheckBoxCheckBackgroundFillUncheckedPointerOver",
        "CheckBoxCheckBackgroundFillUncheckedPressed",
    ];

    public static void Configure(MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        EntryHandler.Mapper.AppendToMapping(FieldChromeMappingKey, (handler, _) =>
        {
            handler.PlatformView.BorderThickness = new PlatformThickness(0);
            handler.PlatformView.Resources[FocusedBorderThicknessKey] = new PlatformThickness(0);
        });
        EditorHandler.Mapper.AppendToMapping(FieldChromeMappingKey, (handler, _) =>
        {
            handler.PlatformView.BorderThickness = new PlatformThickness(0);
            handler.PlatformView.Resources[FocusedBorderThicknessKey] = new PlatformThickness(0);
        });
        CheckBoxHandler.Mapper.AppendToMapping(CheckBoxLookMappingKey, (handler, _) =>
        {
            if (Microsoft.Maui.Controls.Application.Current?.Resources.TryGetValue(CheckBoxLook.FillKey, out var value) == true && value is Color fill)
            {
                var brush = new PlatformBrush(fill.ToWindowsColor());
                foreach (var key in _uncheckedFillKeys)
                {
                    handler.PlatformView.Resources[key] = brush;
                }
            }

            handler.PlatformView.Loaded -= FitCheckBoxBox;
            handler.PlatformView.Loaded += FitCheckBoxBox;
        });
        LabelHandler.Mapper.AppendToMapping(TextSelection.MappingKey, (handler, view) =>
        {
            if (view is BindableObject label)
            {
                handler.PlatformView.IsTextSelectionEnabled = TextSelection.GetIsEnabled(label);
            }
        });
    }

    private static void FitCheckBoxBox(object sender, RoutedEventArgs e)
    {
        if (sender is not PlatformCheckBox box
            || CheckBoxLook.InsetFor(box.Width, box.Height) is not { } inset
            || VisualTreeHelper.GetChildrenCount(box) == 0
            || VisualTreeHelper.GetChild(box, 0) is not FrameworkElement root)
        {
            return;
        }

        root.Margin = new PlatformThickness(inset);
        if (FindPart(root, CheckBoxBoxPartName) is Rectangle part)
        {
            part.StrokeThickness = CheckBoxLook.BorderWidth;
        }
    }

    private static FrameworkElement? FindPart(DependencyObject parent, string name)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement element && element.Name == name)
            {
                return element;
            }

            if (FindPart(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
