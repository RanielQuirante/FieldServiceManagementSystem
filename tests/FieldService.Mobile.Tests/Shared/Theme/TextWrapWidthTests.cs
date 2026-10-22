using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class TextWrapWidthTests
{
    private const double Density = 2.625;
    private const double CardInset = 21;
    private const double DescriptionWidth = 337.43;
    private const int ThreeLines = 182;
    private const int FourLines = 244;

    [Fact]
    public void TheDescriptionCardIsArrangedOnePixelNarrowerThanItIsMeasuredSoItsLabelIsMeasuredAgainAtTheArrangedWidth()
    {
        var spec = MeasuredPixels(DescriptionWidth);
        var arranged = ArrangedPixels(CardInset, DescriptionWidth);

        var next = TextWrapWidth.None.After(spec, arranged, FourLines, ThreeLines);

        Assert.Equal(886, spec);
        Assert.Equal(885, arranged);
        Assert.Equal(new TextWrapWidth(886, 885), next);
        Assert.True(TextWrapWidth.None.Remeasures(next, spec));
        Assert.Equal(885, next.WidthFor(spec));
    }

    [Fact]
    public void OnceTheArrangedWidthIsKnownTheLabelKeepsMeasuringAtItUntilItsConstraintChanges()
    {
        var learned = new TextWrapWidth(886, 885);

        var settled = learned.After(886, 885, FourLines, FourLines);
        var resized = learned.After(900, 899, ThreeLines, ThreeLines);

        Assert.Equal(learned, settled);
        Assert.False(learned.Remeasures(settled, 886));
        Assert.Equal(885, settled.WidthFor(886));
        Assert.Equal(TextWrapWidth.None, resized);
        Assert.Equal(900, resized.WidthFor(900));
        Assert.Equal(900, learned.WidthFor(900));
    }

    [Theory]
    [InlineData(886, 886, FourLines, ThreeLines)]
    [InlineData(886, 885, ThreeLines, ThreeLines)]
    [InlineData(886, 885, ThreeLines + 1, ThreeLines)]
    [InlineData(886, 880, FourLines, ThreeLines)]
    [InlineData(0, 0, FourLines, ThreeLines)]
    public void ALabelThatFitsIsArrangedAtItsMeasuredWidthOrIsNarrowedByLayoutIsLeftAlone(int spec, int arranged, int needed, int height)
    {
        var next = TextWrapWidth.None.After(spec, arranged, needed, height);

        Assert.Equal(TextWrapWidth.None, next);
        Assert.False(TextWrapWidth.None.Remeasures(next, spec));
        Assert.Equal(spec, next.WidthFor(spec));
    }

    [Fact]
    public void TheMeasureWidthOnlyEverNarrowsForTheSameConstraint()
    {
        var learned = new TextWrapWidth(886, 885);

        Assert.Equal(new TextWrapWidth(886, 884), learned.After(886, 884, FourLines, ThreeLines));
        Assert.Equal(learned, learned.After(886, 885, FourLines + 60, FourLines));
    }

    [Fact]
    public void PerEdgeRoundingLosesAtMostOnePixelAtEveryDensityAndOffsetTheAppUses()
    {
        double[] densities = [1, 1.5, 2, 2.625, 2.75, 3, 3.5, 4];
        var losses = new HashSet<int>();
        foreach (var density in densities)
        {
            for (var inset = 0d; inset <= 40; inset += 0.25)
            {
                for (var width = 300d; width <= 420; width += 0.140625)
                {
                    losses.Add(MeasuredPixels(width, density) - ArrangedPixels(inset, width, density));
                }
            }
        }

        Assert.Subset(new HashSet<int> { 0, 1 }, losses);
        Assert.Contains(1, losses);
        Assert.True(losses.Max() <= TextWrapWidth.MaxRoundingLoss);
    }

    [Fact]
    public void EveryLabelOnAndroidIsMeasuredAtTheWidthItIsDrawnAt()
    {
        var setup = Source("Platforms/Android/AndroidPlatformSetup.cs");
        var handler = Source("Platforms/Android/WrapSafeLabelHandler.cs");

        Assert.Contains("handlers.AddHandler<Label, WrapSafeLabelHandler>();", setup, StringComparison.Ordinal);
        Assert.Contains("public sealed class WrapSafeLabelHandler : LabelHandler", handler, StringComparison.Ordinal);
        Assert.Contains("protected override MauiTextView CreatePlatformView() => new WrapSafeTextView(Context);", handler, StringComparison.Ordinal);
        Assert.Contains("var width = _wrap.WidthFor(_spec);", handler, StringComparison.Ordinal);
        Assert.Contains("if (widthMode != MeasureSpecMode.Unspecified && !arrangePass)", handler, StringComparison.Ordinal);
        Assert.Contains("var lines = MaxLines > 0 ? Math.Min(layout.LineCount, MaxLines) : layout.LineCount;", handler, StringComparison.Ordinal);
        Assert.Contains("var next = _wrap.After(_spec, right - left, needed, bottom - top);", handler, StringComparison.Ordinal);
        Assert.Contains("Post(RequestLayout);", handler, StringComparison.Ordinal);
        Assert.DoesNotContain(RepositoryPaths.MobileSources("*.cs"), path => File.ReadAllText(path).Contains("AddHandler<Label,", StringComparison.Ordinal) && !path.EndsWith("AndroidPlatformSetup.cs", StringComparison.Ordinal));
    }

    private static int MeasuredPixels(double width, double density = Density) => (int)Math.Ceiling(width * density);

    private static int ArrangedPixels(double inset, double width, double density = Density) =>
        (int)(Math.Ceiling((inset + width) * density) - Math.Ceiling(inset * density));

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));
}
