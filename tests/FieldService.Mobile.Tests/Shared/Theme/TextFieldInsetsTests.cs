using System.Globalization;
using System.Xml.Linq;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class TextFieldInsetsTests
{
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2009/xaml";
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";

    [Fact]
    public void OnWindowsTheTextBoxLosesWinUisSidePaddingAndKeepsItsVerticalPaddingSoSingleLineFieldsStayCentred()
    {
        Assert.Equal(new Thickness(10, 5, 6, 6), TextFieldInsets.WinUiTextBoxPadding);
        Assert.Equal(new Thickness(0, 5, 0, 6), TextFieldInsets.WindowsEntryPadding);
        Assert.Equal(TextFieldInsets.WinUiTextBoxPadding.Top, TextFieldInsets.WindowsEntryPadding.Top);
        Assert.Equal(TextFieldInsets.WinUiTextBoxPadding.Bottom, TextFieldInsets.WindowsEntryPadding.Bottom);
    }

    [Fact]
    public void WithWinUisPaddingGoneTheTextStarts16InsideTheBorderOr48WithALeadingIcon()
    {
        var content = Metric("FsMarginInputContent");
        var icon = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Controls", "FormField.xaml"))
            .Descendants(_controls + "Icon").Single(element => element.Attribute("IsVisible")?.Value == "{Binding HasIcon}");
        var margin = icon.Attribute("Margin")!.Value.Split(',').Select(part => double.Parse(part, CultureInfo.InvariantCulture)).ToArray();
        var iconColumn = margin[0] + double.Parse(icon.Attribute("Size")!.Value, CultureInfo.InvariantCulture) + margin[2];
        var source = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Controls", "FormField.xaml.cs"));

        Assert.Equal(1.5, content);
        Assert.Equal(48, iconColumn);
        Assert.Contains("private static readonly Thickness _leadingInset = new(16, 0, 0, 0);", source, StringComparison.Ordinal);
        Assert.Equal(1.5 + 16, TextFieldInsets.TextStart(content, 16, TextFieldInsets.WindowsEntryPadding.Left));
        Assert.Equal(1.5 + 48, TextFieldInsets.TextStart(content, iconColumn, TextFieldInsets.WindowsEntryPadding.Left));
        Assert.Equal(1.5 + 16 + 10, TextFieldInsets.TextStart(content, 16, TextFieldInsets.WinUiTextBoxPadding.Left));
    }

    [Fact]
    public void OnWindowsTheEditorsFirstLineDrops10ToSitUnderThe14TopPaddingAsOnAndroid()
    {
        var content = Metric("FsMarginInputContent");
        var editor = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Theme", "Metrics.xaml")).Root!.Elements()
            .Single(element => element.Attribute(_xaml + "Key")?.Value == "FsMarginEditor").Value;

        Assert.Equal("16,2", editor);
        Assert.Equal(new Thickness(0, 15, 0, 6), TextFieldInsets.WindowsEditorPadding);
        Assert.Equal(10, TextFieldInsets.EditorFirstLineDrop);
        Assert.Equal(8.5, TextFieldInsets.FirstLineTop(content, 2, TextFieldInsets.WinUiTextBoxPadding.Top));
        Assert.Equal(18.5, TextFieldInsets.FirstLineTop(content, 2, TextFieldInsets.WindowsEditorPadding.Top));
        Assert.Equal(1.5 + 16, TextFieldInsets.TextStart(content, 16, TextFieldInsets.WindowsEditorPadding.Left));
    }

    [Fact]
    public void TheWindowsFieldMappingsSetThePaddingAndAndroidKeepsItsOwnFieldChrome()
    {
        var windows = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Platforms", "Windows", "WindowsPlatformSetup.cs"));
        var android = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Platforms", "Android", "AndroidPlatformSetup.cs"));
        var entry = windows[windows.IndexOf("EntryHandler.Mapper.AppendToMapping(FieldChromeMappingKey", StringComparison.Ordinal)..windows.IndexOf("EditorHandler.Mapper.AppendToMapping(FieldChromeMappingKey", StringComparison.Ordinal)];
        var editor = windows[windows.IndexOf("EditorHandler.Mapper.AppendToMapping(FieldChromeMappingKey", StringComparison.Ordinal)..windows.IndexOf("CheckBoxHandler.Mapper.AppendToMapping", StringComparison.Ordinal)];

        Assert.Contains("handler.PlatformView.Padding = ToPlatform(TextFieldInsets.WindowsEntryPadding);", entry, StringComparison.Ordinal);
        Assert.Contains("handler.PlatformView.Padding = ToPlatform(TextFieldInsets.WindowsEditorPadding);", editor, StringComparison.Ordinal);
        Assert.Contains("private static PlatformThickness ToPlatform(Microsoft.Maui.Thickness thickness) => new(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);", windows, StringComparison.Ordinal);
        Assert.DoesNotContain("TextFieldInsets", android, StringComparison.Ordinal);
        Assert.Contains("platformView.SetPadding(0, platformView.PaddingTop, 0, platformView.PaddingBottom);", android, StringComparison.Ordinal);
    }

    private static double Metric(string key) =>
        double.Parse(
            XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Theme", "Metrics.xaml")).Root!.Elements()
                .Single(element => element.Attribute(_xaml + "Key")?.Value == key).Value,
            CultureInfo.InvariantCulture);
}
