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
    private static readonly Thickness _padding = new(22, 8);

    [Fact]
    public void ThePrimaryButtonPaddingKeeps8AboveAndBelowAndBothTheCoreButtonAndTheOverlayUseIt()
    {
        var button = Load("Shared/Controls/PrimaryButton.xaml");
        var core = button.Descendants(_maui + "Button").Single(element => element.Attribute(_xaml + "Name")?.Value == "CoreButton");
        var overlay = button.Descendants(_controls + "IconLabelLayout").Single();
        var metric = Load("Shared/Theme/Metrics.xaml").Root!.Elements().Single(element => element.Attribute(_xaml + "Key")?.Value == "FsPaddingPrimaryButton");

        Assert.Equal("22,8", metric.Value);
        Assert.Equal("{StaticResource FsPaddingPrimaryButton}", core.Attribute("Padding")?.Value);
        Assert.Equal("{StaticResource FsPaddingPrimaryButton}", overlay.Attribute("Padding")?.Value);
        Assert.Equal("{StaticResource FsHeightPrimary}", core.Attribute("MinimumHeightRequest")?.Value);
    }

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
        Assert.Equal(285.714 - 44 - 30, IconLabelLayout.LabelWidthFor(285.714, _padding, 30), 3);
        Assert.Equal(double.PositiveInfinity, IconLabelLayout.LabelWidthFor(double.PositiveInfinity, _padding, 30));
        Assert.Equal(0, IconLabelLayout.LabelWidthFor(40, _padding, 30));
    }

    [Theory]
    [InlineData(23.407, 16.296)]
    [InlineData(28.741, 13.63)]
    [InlineData(36.741, 9.63)]
    [InlineData(39.704, 8.148)]
    [InlineData(40, 8)]
    public void AOneLineButtonStays56TallWithItsLabelWhereItWasAtEveryScale(double lineHeight, double deviceTop)
    {
        var cell = new FixedView(30, lineHeight);
        var label = new WrappingLabel(oneLineWidth: 150.519, lineHeight: lineHeight, twoLineHeight: lineHeight * 2, wrappedLineWidth: 0);
        var layout = new IconLabelLayout { Padding = _padding };
        layout.Add(cell);
        layout.Add(label);
        var cross = (ICrossPlatformLayout)layout;

        var measured = cross.CrossPlatformMeasure(286.222, double.PositiveInfinity);
        cross.CrossPlatformArrange(new Rect(0, 0, 286.222, Math.Max(56, measured.Height)));

        Assert.True(measured.Height <= 56);
        Assert.Equal(lineHeight + 16, measured.Height, 3);
        Assert.Equal(deviceTop, label.Frame.Y, 0.01);
        Assert.Equal(deviceTop, cell.Frame.Y, 0.01);
        Assert.Equal(TextAlignment.Center, label.HorizontalTextAlignment);
    }

    [Theory]
    [InlineData(377.143, 23.238, 149.333, 23.238, 98.81, 129)]
    [InlineData(377.143, 23.238, 174.476, 23.238, 86.238, 116.429)]
    [InlineData(377.143, 23.238, 70.095, 23.238, 138.429, 168.619)]
    [InlineData(377.143, 39.619, 301.714, 40, 22.619, 52.81)]
    [InlineData(285.714, 23.238, 149.333, 23.238, 53.095, 83.286)]
    [InlineData(285.714, 23.238, 158.857, 23.238, 48.333, 78.524)]
    [InlineData(285.714, 28.571, 184.762, 28.571, 35.381, 65.571)]
    [InlineData(285.714, 36.952, 111.238, 36.952, 72.143, 102.333)]
    [InlineData(285.714, 38.476, 115.048, 38.476, 70.238, 100.429)]
    [InlineData(285.714, 39.619, 120, 40, 67.762, 97.952)]
    public void AOneLineLabelKeepsTheCentredGroupWithTheIconTenBeforeItAndOnItsCentre(double buttonWidth, double lineHeight, double labelWidth, double labelHeight, double deviceIconX, double deviceLabelX)
    {
        var bounds = new Rect(0, 0, buttonWidth, 56);

        var wrapped = IconLabelLayout.Wraps(labelHeight, lineHeight);
        var (icon, label) = IconLabelLayout.Place(bounds, _padding, new Size(30, lineHeight), new Size(labelWidth, labelHeight), wrapped);

        Assert.False(wrapped);
        Assert.Equal(TextAlignment.Center, IconLabelLayout.AlignmentFor(wrapped));
        Assert.Equal(22 + ((buttonWidth - 44 - 30 - labelWidth) / 2), icon.X, 3);
        Assert.Equal(buttonWidth, label.Right + icon.X, 3);
        Assert.Equal(icon.Right, label.X, 3);
        Assert.Equal(labelWidth, label.Width, 3);
        Assert.Equal((56 - Math.Max(lineHeight, labelHeight)) / 2, icon.Y, 3);
        Assert.Equal(icon.Y, label.Y, 3);
        Assert.InRange(icon.Center.Y - label.Center.Y, -1, 1);
        Assert.Equal(deviceIconX, icon.X, 0.4);
        Assert.Equal(deviceLabelX, label.X, 0.4);
    }

    [Theory]
    [InlineData(286.222, 28.741, 212.074, 56, 72)]
    [InlineData(286.222, 36.741, 212.074, 71.704, 87.704)]
    [InlineData(286.222, 39.704, 212.074, 77.333, 93.333)]
    [InlineData(285.714, 28.571, 211.524, 55.619, 71.619)]
    [InlineData(285.714, 36.952, 211.524, 72, 88)]
    [InlineData(285.714, 38.476, 211.524, 75.048, 91.048)]
    [InlineData(285.714, 39.619, 211.524, 77.333, 93.333)]
    [InlineData(285.714, 39.619, 180.5, 77.333, 93.333)]
    public void AWrappedLabelIsOneStartAlignedBlockEightBelowTheTopAndEightAboveTheBottomWithTheIconLevelWithTheFirstLine(double buttonWidth, double lineHeight, double labelWidth, double labelHeight, double buttonHeight)
    {
        var height = Math.Max(56, labelHeight + _padding.VerticalThickness);
        var bounds = new Rect(0, 0, buttonWidth, height);

        var wrapped = IconLabelLayout.Wraps(labelHeight, lineHeight);
        var (icon, label) = IconLabelLayout.Place(bounds, _padding, new Size(30, lineHeight), new Size(labelWidth, labelHeight), wrapped);

        Assert.True(wrapped);
        Assert.Equal(buttonHeight, height, 3);
        Assert.Equal(TextAlignment.Start, IconLabelLayout.AlignmentFor(wrapped));
        Assert.Equal(22, icon.X, 3);
        Assert.Equal(52, label.X, 3);
        Assert.Equal(buttonWidth - 44 - 30, label.Width, 3);
        Assert.Equal(8, label.Y, 3);
        Assert.Equal(8, icon.Y, 3);
        Assert.Equal(8, height - label.Bottom, 3);
        Assert.InRange(icon.Center.Y - (label.Y + (lineHeight / 2)), -1, 1);
    }

    [Theory]
    [InlineData(23.238, 23.238, false)]
    [InlineData(40, 39.619, false)]
    [InlineData(42.75, 28.5, false)]
    [InlineData(42.76, 28.5, true)]
    [InlineData(55.619, 28.571, true)]
    [InlineData(77.333, 39.619, true)]
    [InlineData(77.333, 0, false)]
    public void ALabelCountsAsWrappedOnlyWhenItIsTallerThanOneAndAHalfOfItsLines(double labelHeight, double lineHeight, bool wraps)
    {
        Assert.Equal(1.5, IconLabelLayout.WrapRatio);
        Assert.Equal(wraps, IconLabelLayout.Wraps(labelHeight, lineHeight));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(180.5)]
    public void TheLayoutDecidesFromTheLabelMeasuredAgainAtTheWidthItArrangesAt(double wrappedLineWidth)
    {
        var cell = new FixedView(30, 39.619);
        var label = new WrappingLabel(oneLineWidth: 301.714, lineHeight: 40, twoLineHeight: 77.333, wrappedLineWidth) { HorizontalTextAlignment = TextAlignment.Center };
        var layout = new IconLabelLayout { Padding = _padding };
        layout.Add(cell);
        layout.Add(label);
        var cross = (ICrossPlatformLayout)layout;

        var measured = cross.CrossPlatformMeasure(377.143, double.PositiveInfinity);
        cross.CrossPlatformArrange(new Rect(0, 0, 285.714, 93.333));
        var wrappedAlignment = label.HorizontalTextAlignment;
        var (wrappedCell, wrappedLabel) = (cell.Frame, label.Frame);
        cross.CrossPlatformMeasure(377.143, double.PositiveInfinity);
        cross.CrossPlatformArrange(new Rect(0, 0, 377.143, 56));

        Assert.Equal(56, measured.Height, 0.001);
        Assert.Equal(4, label.Constraints.Count);
        Assert.Equal(211.714, label.Constraints[1], 0.001);
        Assert.Equal(TextAlignment.Start, wrappedAlignment);
        Assert.Equal((22.0, 8.0, 30.0), (wrappedCell.X, wrappedCell.Y, wrappedCell.Width));
        Assert.Equal(8, wrappedLabel.Y, 0.001);
        Assert.Equal(52, wrappedLabel.X, 0.001);
        Assert.Equal(211.714, wrappedLabel.Width, 0.001);
        Assert.Equal(77.333, wrappedLabel.Height, 0.001);
        Assert.Equal(303.143, label.Constraints[^1], 0.001);
        Assert.Equal(TextAlignment.Center, label.HorizontalTextAlignment);
        Assert.Equal(22.7145, cell.Frame.X, 0.001);
        Assert.Equal(52.7145, label.Frame.X, 0.001);
        Assert.Equal(301.714, label.Frame.Width, 0.001);
        Assert.Equal(8, label.Frame.Y, 0.001);
        Assert.Equal(8, cell.Frame.Y, 0.001);
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

    private sealed class FixedView(double width, double height) : View
    {
        protected override Size MeasureOverride(double widthConstraint, double heightConstraint) => new(width, height);
    }

    private sealed class WrappingLabel(double oneLineWidth, double lineHeight, double twoLineHeight, double wrappedLineWidth) : Label
    {
        public List<double> Constraints { get; } = [];

        protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
        {
            Constraints.Add(widthConstraint);
            return widthConstraint >= oneLineWidth
                ? new Size(oneLineWidth, lineHeight)
                : new Size(wrappedLineWidth > 0 ? wrappedLineWidth : widthConstraint, twoLineHeight);
        }
    }
}
