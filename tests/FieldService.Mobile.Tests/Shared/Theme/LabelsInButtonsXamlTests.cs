using System.Xml.Linq;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class LabelsInButtonsXamlTests
{
    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2009/xaml";
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";

    [Fact]
    public void APrimaryButtonsIconAndLabelSitInACentredRowWhoseLabelWrapsInsideTheButton()
    {
        var button = Load("Shared/Controls/PrimaryButton.xaml");
        var grid = button.Root!.Elements(_maui + "Grid").Single();
        var overlay = grid.Elements(_controls + "IconLabelLayout").Single();
        var children = overlay.Elements().ToList();
        var cell = children[0];
        var label = children[1];

        Assert.Empty(grid.Elements(_maui + "HorizontalStackLayout"));
        Assert.Equal("{StaticResource FsHeightPrimary}", grid.Attribute("MinimumHeightRequest")?.Value);
        Assert.Equal("{StaticResource FsPaddingPrimaryButton}", overlay.Attribute("Padding")?.Value);
        Assert.Equal("{Binding HasIcon}", overlay.Attribute("IsVisible")?.Value);
        Assert.Equal("True", overlay.Attribute("InputTransparent")?.Value);
        Assert.Equal("True", overlay.Attribute("AutomationProperties.ExcludedWithChildren")?.Value);
        Assert.Equal(2, children.Count);
        Assert.Equal("Grid", cell.Name.LocalName);
        Assert.Equal("20", cell.Attribute("WidthRequest")?.Value);
        Assert.Equal("0,0,10,0", cell.Attribute("Margin")?.Value);
        var box = cell.Elements(_maui + "Label").Single();
        var icon = cell.Elements(_controls + "Icon").Single();
        Assert.Equal("{StaticResource FsTextButtonLabel}", box.Attribute("Style")?.Value);
        Assert.Equal(("A", "1", "NoWrap", "0", "True", "False"), (box.Attribute("Text")?.Value, box.Attribute("MaxLines")?.Value, box.Attribute("LineBreakMode")?.Value, box.Attribute("Opacity")?.Value, box.Attribute("InputTransparent")?.Value, box.Attribute("AutomationProperties.IsInAccessibleTree")?.Value));
        Assert.Equal("20", icon.Attribute("Size")?.Value);
        Assert.Equal("Center", icon.Attribute("VerticalOptions")?.Value);
        Assert.Equal("Label", label.Name.LocalName);
        Assert.Equal("{StaticResource FsTextButtonLabel}", label.Attribute("Style")?.Value);
        Assert.Equal("WordWrap", label.Attribute("LineBreakMode")?.Value);
        Assert.Equal("Center", label.Attribute("HorizontalTextAlignment")?.Value);
    }

    [Fact]
    public void TheLabelGetsTheButtonWidthLessThePaddingAndTheIconCell()
    {
        var padding = new Thickness(22, 0);

        Assert.Equal(285.714 - 44 - 30, IconLabelLayout.LabelWidthFor(285.714, padding, 30), 3);
        Assert.Equal(double.PositiveInfinity, IconLabelLayout.LabelWidthFor(double.PositiveInfinity, padding, 30));
        Assert.Equal(0, IconLabelLayout.LabelWidthFor(40, padding, 30));
    }

    [Fact]
    public void TheIconAndLabelAreCentredAsAGroupWithTheIconBesideTheFirstLine()
    {
        var padding = new Thickness(22, 0);
        var bounds = new Rect(0, 0, 285.714, 56);

        var (icon, label) = IconLabelLayout.Place(bounds, padding, new Size(30, 23.6), new Size(149.333, 23.6));
        var (wrappedIcon, wrappedLabel) = IconLabelLayout.Place(new Rect(0, 0, 285.714, 77.333), padding, new Size(30, 38.7), new Size(211.714, 77.333));

        Assert.Equal((285.714 - 30 - 149.333) / 2, icon.X, 3);
        Assert.Equal(icon.Right, label.X, 3);
        Assert.Equal(285.714, label.Right + icon.X, 3);
        Assert.Equal((56 - 23.6) / 2, icon.Y, 3);
        Assert.Equal(icon.Y, label.Y, 3);
        Assert.Equal(0, wrappedIcon.Y, 3);
        Assert.Equal(0, wrappedLabel.Y, 3);
        Assert.Equal(211.714, wrappedLabel.Width, 3);
        Assert.Equal(22, wrappedIcon.X, 3);
    }

    [Fact]
    public void APrimaryButtonWithoutAnIconWrapsItsOwnText()
    {
        var core = Load("Shared/Controls/PrimaryButton.xaml").Descendants(_maui + "Button").Single(element => element.Attribute(_xaml + "Name")?.Value == "CoreButton");

        Assert.Equal("WordWrap", core.Attribute("LineBreakMode")?.Value);
        Assert.Equal("{Binding ButtonText}", core.Attribute("Text")?.Value);
    }

    [Fact]
    public void ThePriorityGlyphLabelKeepsOneUnitAfterItsLastGlyph()
    {
        var label = Load("Shared/Controls/PriorityLabel.xaml").Descendants(_maui + "Label").Single(element => element.Attribute(_xaml + "Name")?.Value == "GlyphLabel");

        Assert.Equal("0,0,1,0", label.Attribute("Padding")?.Value);
    }

    private static XDocument Load(string relative) =>
        XDocument.Load(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));
}
