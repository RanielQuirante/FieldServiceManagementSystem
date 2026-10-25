using System.Xml.Linq;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class CheckBoxLookTests
{
    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2009/xaml";

    [Fact]
    public void TheWindowsBoxIsTwentyWithAOneAndAHalfBorderOnAWhiteFill()
    {
        var colours = Load("Shared/Theme/Colors.xaml");

        Assert.Equal(20, CheckBoxLook.BoxSize);
        Assert.Equal(1.5, CheckBoxLook.BorderWidth);
        Assert.Equal("FsSurface", CheckBoxLook.FillKey);
        Assert.Equal("#FFFFFF", Colour(colours, CheckBoxLook.FillKey));
        Assert.Equal("#0F766E", Colour(colours, "FsTeal700"));
    }

    [Fact]
    public void TheBoxSitsCentredInTheRowsTwentyTwoSlotWithNoInsetLeftFromMauisThirtyTwoHighDefault()
    {
        var box = Load("Shared/Controls/CheckboxRow.xaml").Descendants(_maui + "CheckBox").Single();
        var width = double.Parse(box.Attribute("WidthRequest")!.Value, System.Globalization.CultureInfo.InvariantCulture);
        var height = double.Parse(box.Attribute("HeightRequest")!.Value, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal("{StaticResource FsTeal700}", box.Attribute("Color")?.Value);
        Assert.Equal(1, CheckBoxLook.InsetFor(width, height));
        Assert.Equal(6, CheckBoxLook.InsetFor(32, 32));
        Assert.Equal(1, CheckBoxLook.InsetFor(22, 40));
        Assert.Equal(0, CheckBoxLook.InsetFor(18, 18));
        Assert.Null(CheckBoxLook.InsetFor(double.NaN, 22));
        Assert.Null(CheckBoxLook.InsetFor(22, -1));
        Assert.Null(CheckBoxLook.InsetFor(double.PositiveInfinity, 22));
    }

    [Fact]
    public void OnWindowsTheCheckBoxGetsTheWhiteFillAndTheBorderAndLosesMauisSixUnitInsetOnceLoaded()
    {
        var setup = Source("Platforms/Windows/WindowsPlatformSetup.cs");

        Assert.Contains("CheckBoxHandler.Mapper.AppendToMapping(CheckBoxLookMappingKey", setup, StringComparison.Ordinal);
        Assert.Contains("TryGetValue(CheckBoxLook.FillKey, out var value)", setup, StringComparison.Ordinal);
        foreach (var key in new[] { "CheckBoxCheckBackgroundFillUnchecked", "CheckBoxCheckBackgroundFillUncheckedPointerOver", "CheckBoxCheckBackgroundFillUncheckedPressed" })
        {
            Assert.Contains($"\"{key}\",", setup, StringComparison.Ordinal);
        }

        Assert.Contains("handler.PlatformView.Resources[key] = brush;", setup, StringComparison.Ordinal);
        Assert.Contains("handler.PlatformView.Loaded += FitCheckBoxBox;", setup, StringComparison.Ordinal);
        var fit = setup[setup.IndexOf("private static void FitCheckBoxBox", StringComparison.Ordinal)..];
        Assert.Contains("CheckBoxLook.InsetFor(box.Width, box.Height) is not { } inset", fit, StringComparison.Ordinal);
        Assert.Contains("VisualTreeHelper.GetChild(box, 0) is not FrameworkElement root", fit, StringComparison.Ordinal);
        Assert.Contains("root.Margin = new PlatformThickness(inset);", fit, StringComparison.Ordinal);
        Assert.Contains("FindPart(root, CheckBoxBoxPartName) is Rectangle part", fit, StringComparison.Ordinal);
        Assert.Contains("part.StrokeThickness = CheckBoxLook.BorderWidth;", fit, StringComparison.Ordinal);
        Assert.Contains("public const string CheckBoxBoxPartName = \"NormalRectangle\";", setup, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidKeepsItsOwnCheckBoxLook()
    {
        var android = RepositoryPaths.MobileSources("*.cs")
            .Where(path => RepositoryPaths.ToRelative(path).StartsWith("Platforms/Android/", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("CheckBoxLook", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(android);
    }

    private static string? Colour(XDocument colours, string key) =>
        colours.Root!.Elements(_maui + "Color").Single(colour => colour.Attribute(_xaml + "Key")?.Value == key).Value;

    private static XDocument Load(string relative) =>
        XDocument.Load(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));
}
