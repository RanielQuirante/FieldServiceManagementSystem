using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class WindowsLineHeightTests
{
    [Theory]
    [InlineData(1.05, 1.35)]
    [InlineData(1.1, 1.425)]
    [InlineData(1.15, 1.5)]
    [InlineData(1.2, 1.56)]
    public void EachThemeLineHeightMapsBackToTheSpecValueItWasRoundedFrom(double theme, double spec) =>
        Assert.Equal(spec, LineHeights.SpecFor(theme), 3);

    [Theory]
    [InlineData(1.15, 15, 1.0, 22.5)]
    [InlineData(1.1, 16, 1.0, 22.8)]
    [InlineData(1.1, 16, 1.5, 34.2)]
    [InlineData(1.15, 15, 1.5, 33.75)]
    [InlineData(1.1, 16, 2.25, 51.3)]
    [InlineData(1.05, 17, 2.25, 51.638)]
    public void OnWindowsALinesPitchIsTheSpecValueTimesTheFontSizeAsRendered(double theme, double fontSize, double textScale, double pitch)
    {
        var natural = LineHeights.FontNaturalLineHeight * fontSize * textScale;

        var lineHeight = LineHeights.WindowsLineHeight(theme, natural);

        Assert.Equal(pitch, lineHeight, 2);
        Assert.InRange(lineHeight, (LineHeights.SpecFor(theme) * fontSize * textScale) - 1, (LineHeights.SpecFor(theme) * fontSize * textScale) + 1);
    }

    [Theory]
    [InlineData(1.0, 22.5)]
    [InlineData(1.5, 33.75)]
    [InlineData(2.25, 50.625)]
    public void TheReviewsFailedItemNoteTakesTheBoardsOneAndAHalfLinesOnWindowsAtEveryTextSize(double textScale, double pitch)
    {
        var page = System.Xml.Linq.XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Features", "Inspection", "ReviewAndSubmitPage.xaml"));
        System.Xml.Linq.XNamespace controls = "clr-namespace:FieldService.Mobile.Shared.Controls";
        var note = page.Descendants().Single(element => element.Attribute("Text")?.Value == "{Binding Note}");

        Assert.Equal("1.5", note.Attribute(controls + "LineHeights.Spec")?.Value);
        Assert.Equal(pitch, LineHeights.WindowsLineHeight(1.1, 1.5, LineHeights.FontNaturalLineHeight * 15 * textScale), 2);
        Assert.Equal(1.425, LineHeights.SpecFor(1.1, 0), 3);
        Assert.Equal(1.425, LineHeights.SpecFor(-1 + 2.1, 0), 3);
    }

    [Theory]
    [InlineData(-1, 20.8)]
    [InlineData(0, 20.8)]
    [InlineData(1.1, 0)]
    public void ALabelWithNoThemeLineHeightKeepsTheFontsNaturalLine(double theme, double natural) =>
        Assert.Equal(0, LineHeights.WindowsLineHeight(theme, natural));

    [Fact]
    public void WindowsSetsEachLabelsBlockLineHeightFromItsRenderedSizeAndUpdatesItWhenTheTextSizeChanges()
    {
        var setup = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Platforms", "Windows", "WindowsPlatformSetup.cs"));
        var heights = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Platforms", "Windows", "WindowsLineHeights.cs"));

        Assert.Contains("foreach (var key in new[] { nameof(ILabel.LineHeight), nameof(ITextStyle.Font), LineHeights.SpecProperty.PropertyName })", setup, StringComparison.Ordinal);
        Assert.Contains("WindowsLineHeights.Apply(handler.PlatformView, label.LineHeight, LineHeights.GetSpec(label));", setup, StringComparison.Ordinal);
        Assert.Contains("text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;", heights, StringComparison.Ordinal);
        Assert.Contains("LineHeights.WindowsLineHeight(wanted.ThemeLineHeight, wanted.Spec, Natural(text))", heights, StringComparison.Ordinal);
        Assert.Contains("IsTextScaleFactorEnabled = text.IsTextScaleFactorEnabled,", heights, StringComparison.Ordinal);
        Assert.Contains("_settings.TextScaleFactorChanged +=", heights, StringComparison.Ordinal);
        Assert.Contains("_natural.Clear();", heights, StringComparison.Ordinal);
    }
}
