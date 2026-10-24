using System.Xml.Linq;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class CardHeadLayoutTests
{
    private const double RowAt320 = 250;
    private const double RowAt411 = 341.429;
    private const double NumberLine = 18.2;
    private const double ChipHeight = 32;
    private const double NumberAt13 = 91.5556;
    private const double InProgressAt13 = 146.6667;
    private const double MeasuredAt845Px = 250.3704;
    private const double ArrangedAt844Px = 250.0741;
    private const double HalfNarrower = MeasuredAt845Px - 0.5;

    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2009/xaml";
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";

    [Theory]
    [InlineData(88.5, 149.5, RowAt320, false)]
    [InlineData(88.5, 149.51, RowAt320, true)]
    [InlineData(88.7, 144, RowAt320, false)]
    [InlineData(127, 184, RowAt411, false)]
    [InlineData(0, 0, 11.99, true)]
    public void TheChipMovesUnderTheNumberOnlyWhenTheNumberTheTwelveGapAndTheChipAreWiderThanTheRow(double number, double chip, double row, bool twoRows)
    {
        Assert.Equal(12, CardHeadLayout.ChipGap);
        Assert.Equal(twoRows, CardHeadLayout.HeadNeedsTwoRows(number, chip, row));
    }

    [Fact]
    public void OneRowPutsTheNumberAtTheStartAndTheChipAtTheEndBothOnTheRowsHeight()
    {
        var frames = CardHeadLayout.Place(new Rect(10, 10, RowAt320, double.PositiveInfinity), new Size(88.7, NumberLine), new Size(144, ChipHeight), twoRows: false);

        Same(new Rect(10, 10, RowAt320 - 12 - 144, ChipHeight), frames.Number);
        Same(new Rect(10 + RowAt320 - 144, 10, 144, ChipHeight), frames.Chip);
        Assert.Equal(ChipHeight, frames.Height, 3);
    }

    [Fact]
    public void TwoRowsPutTheNumberInTheFirstRowAndTheChipUnderItEightBelowBothAtTheStart()
    {
        var frames = CardHeadLayout.Place(new Rect(10, 10, RowAt320, double.PositiveInfinity), new Size(108, 23.6), new Size(161, 38.5), twoRows: true);

        Assert.Equal(8, CardHeadLayout.RowGap);
        Same(new Rect(10, 10, RowAt320, 23.6), frames.Number);
        Same(new Rect(10, 10 + 23.6 + 8, 161, 38.5), frames.Chip);
        Assert.Equal(23.6 + 8 + 38.5, frames.Height, 3);
    }

    [Theory]
    [InlineData(RowAt320, 88.7, 144, false, 94)]
    [InlineData(RowAt320, 88.7, 144, true, RowAt320)]
    [InlineData(120, 127, 161, true, 127)]
    [InlineData(120, 127, 0, false, 127)]
    public void TheNumbersRoomIsItsColumnOrTheRowAndNeverLessThanItsNaturalWidth(double row, double number, double chip, bool twoRows, double room)
    {
        Assert.Equal(room, CardHeadLayout.NumberRoom(row, number, chip, twoRows), 3);
    }

    [Fact]
    public void TheChipsRoomIsWhatTheNumberLeavesOnOneRowButNeverLessThanTheChipsNaturalWidthAndTheRowOnTwo()
    {
        Assert.Equal(149.3, CardHeadLayout.ChipRoom(RowAt320, 88.7, 144, twoRows: false), 3);
        Assert.Equal(RowAt320, CardHeadLayout.ChipRoom(RowAt320, 88.7, 144, twoRows: true), 3);
        Assert.Equal(InProgressAt13, CardHeadLayout.ChipRoom(HalfNarrower, NumberAt13, InProgressAt13, twoRows: false), 4);
        Assert.Equal(0, CardHeadLayout.ChipRoom(50, 88.7, 0, twoRows: false));
    }

    [Fact]
    public void MeasuredAtTheRowWidthTheHeadKeepsOneRowWhileBothFitAndTheNumberIsMeasuredInItsColumn()
    {
        var parts = Build(88.7, 144);
        var cross = (ICrossPlatformLayout)parts.Head;

        var size = cross.CrossPlatformMeasure(RowAt320, double.PositiveInfinity);
        cross.CrossPlatformArrange(new Rect(0, 0, RowAt320, size.Height));

        Assert.False(parts.Head.IsTwoRows);
        Assert.Equal(new Size(RowAt320, ChipHeight), size);
        Assert.Equal(double.PositiveInfinity, parts.Number.Constraints[0]);
        Assert.Equal(94, parts.Number.Constraints[^1], 3);
        Same(new Rect(0, (ChipHeight - NumberLine) / 2, 94, NumberLine), parts.Number.Frame);
        Same(new Rect(RowAt320 - 144, 0, 144, ChipHeight), parts.Chip.Frame);
    }

    [Fact]
    public void TheModeFlipsBothWaysThroughANewMeasureWhenTheWidthChangesWithTheFramesOfEachMode()
    {
        var parts = Build(88.7, 144);
        var passes = new LayoutPasses(parts.Head);

        passes.Measure(RowAt320);
        passes.Arrange(RowAt320);
        var (wideRows, wideChip) = (parts.Head.IsTwoRows, parts.Chip.Frame);
        var narrow = passes.Measure(200);
        passes.Arrange(200);
        var (narrowRows, narrowNumber, narrowChip) = (parts.Head.IsTwoRows, parts.Number.Frame, parts.Chip.Frame);
        passes.Measure(RowAt320);
        passes.Arrange(RowAt320);

        Assert.False(wideRows);
        Same(new Rect(RowAt320 - 144, 0, 144, ChipHeight), wideChip);
        Assert.True(narrowRows);
        Same(new Rect(0, 0, 200, NumberLine), narrowNumber);
        Same(new Rect(0, NumberLine + 8, 144, ChipHeight), narrowChip);
        Assert.Equal(NumberLine + 8 + ChipHeight, narrow.Height, 3);
        Assert.False(parts.Head.IsTwoRows);
        Same(new Rect(RowAt320 - 144, 0, 144, ChipHeight), parts.Chip.Frame);
        Same(new Rect(0, (ChipHeight - NumberLine) / 2, 94, NumberLine), parts.Number.Frame);
        Assert.Equal(0, passes.Invalidations);
    }

    [Fact]
    public void ArrangedHalfADpNarrowerThanItsOneRowMeasureTheHeadKeepsOneRowInsideItsBoundsAndTheGapPaysTheShortfall()
    {
        var parts = Build(NumberAt13, InProgressAt13);
        var passes = new LayoutPasses(parts.Head);

        passes.Measure(MeasuredAt845Px);
        passes.Arrange(MeasuredAt845Px);
        var steady = passes.Invalidations;
        var bounds = passes.Arrange(HalfNarrower);
        var once = passes.Invalidations;
        passes.Arrange(HalfNarrower);

        Assert.True(CardHeadLayout.HeadNeedsTwoRows(NumberAt13, InProgressAt13, HalfNarrower));
        Assert.False(parts.Head.IsTwoRows);
        Assert.Equal(ChipHeight, bounds.Height, 3);
        Same(new Rect(0, (ChipHeight - NumberLine) / 2, NumberAt13, NumberLine), parts.Number.Frame);
        Same(new Rect(HalfNarrower - InProgressAt13, 0, InProgressAt13, ChipHeight), parts.Chip.Frame);
        Assert.True(parts.Chip.Constraints[^1] >= InProgressAt13, $"the chip measured at {parts.Chip.Constraints[^1]}");
        Assert.Equal(11.6481, parts.Chip.Frame.Left - parts.Number.Frame.Right, 3);
        LayoutPasses.Inside(bounds, parts.Number.Frame, "number");
        LayoutPasses.Inside(bounds, parts.Chip.Frame, "chip");
        Assert.Equal(0, steady);
        Assert.Equal(1, once);
        Assert.Equal(1, passes.Invalidations);
    }

    [Fact]
    public void MeasuredAgainAtTheArrangedWidthTheHeadTakesTwoRowsAndArrangesThem()
    {
        var parts = Build(NumberAt13, InProgressAt13);
        var passes = new LayoutPasses(parts.Head);

        passes.Measure(MeasuredAt845Px);
        passes.Arrange(HalfNarrower);
        var size = passes.Measure(HalfNarrower);
        var bounds = passes.Arrange(HalfNarrower);

        Assert.True(parts.Head.IsTwoRows);
        Assert.Equal(NumberLine + 8 + ChipHeight, size.Height, 3);
        Same(new Rect(0, 0, HalfNarrower, NumberLine), parts.Number.Frame);
        Same(new Rect(0, NumberLine + 8, InProgressAt13, ChipHeight), parts.Chip.Frame);
        LayoutPasses.Inside(bounds, parts.Chip.Frame, "chip");
        Assert.Equal(1, passes.Invalidations);
    }

    [Fact]
    public void MeasuredAt411AndArrangedAt320WithoutANewMeasureTheHeadsFramesStayInsideTheBoundsAndOneInvalidationFollows()
    {
        var parts = Build(NumberAt13, InProgressAt13);
        var passes = new LayoutPasses(parts.Head);

        passes.Measure(RowAt411);
        var bounds = passes.Arrange(RowAt320);
        passes.Arrange(RowAt320);

        Assert.False(parts.Head.IsTwoRows);
        LayoutPasses.Inside(bounds, parts.Number.Frame, "number");
        LayoutPasses.Inside(bounds, parts.Chip.Frame, "chip");
        Assert.True(parts.Number.Frame.Right <= parts.Chip.Frame.Left, $"the number ends at {parts.Number.Frame.Right}, the chip starts at {parts.Chip.Frame.Left}");
        Assert.Equal(1, passes.Invalidations);
    }

    [Fact]
    public void ArrangedWiderThanItsTwoRowMeasureTheHeadKeepsTwoRowsInsideItsBounds()
    {
        var parts = Build(NumberAt13, InProgressAt13);
        var passes = new LayoutPasses(parts.Head);

        passes.Measure(HalfNarrower);
        var bounds = passes.Arrange(MeasuredAt845Px);

        Assert.True(parts.Head.IsTwoRows);
        Assert.Equal(NumberLine + 8 + ChipHeight, bounds.Height, 3);
        LayoutPasses.Inside(bounds, parts.Number.Frame, "number");
        LayoutPasses.Inside(bounds, parts.Chip.Frame, "chip");
        Same(new Rect(0, NumberLine + 8, InProgressAt13, ChipHeight), parts.Chip.Frame);
        Assert.Equal(1, passes.Invalidations);
    }

    [Fact]
    public void AParentThatMeasuresOnePixelWiderThanItArrangesCannotMakeTheHeadFlipBackAndForth()
    {
        var parts = Build(NumberAt13, InProgressAt13);
        var passes = new LayoutPasses(parts.Head);

        var modes = passes.RunParent(MeasuredAt845Px, ArrangedAt844Px, rounds: 10, () => parts.Head.IsTwoRows);

        Assert.True(CardHeadLayout.HeadNeedsTwoRows(NumberAt13, InProgressAt13, ArrangedAt844Px));
        Assert.False(CardHeadLayout.HeadNeedsTwoRows(NumberAt13, InProgressAt13, MeasuredAt845Px));
        Assert.All(modes, Assert.False);
        Assert.Equal(1, passes.Invalidations);
        Assert.Equal(2, passes.Measures);
        Same(new Rect(ArrangedAt844Px - InProgressAt13, 0, InProgressAt13, ChipHeight), parts.Chip.Frame);
    }

    [Theory]
    [InlineData(120)]
    [InlineData(200)]
    [InlineData(RowAt320)]
    public void TheNumbersFrameIsNeverNarrowerThanItsNaturalWidth(double row)
    {
        var parts = Build(127, 161);
        var cross = (ICrossPlatformLayout)parts.Head;

        var size = cross.CrossPlatformMeasure(row, double.PositiveInfinity);
        cross.CrossPlatformArrange(new Rect(0, 0, row, size.Height));

        Assert.True(parts.Head.IsTwoRows);
        Assert.All(parts.Number.Constraints, constraint => Assert.True(constraint >= 127, $"measured at {constraint}"));
        Assert.True(parts.Number.Frame.Width >= 127, $"framed at {parts.Number.Frame.Width}");
        Assert.Equal(127, parts.Number.DesiredSize.Width, 3);
        Same(new Rect(0, NumberLine + 8, 161, ChipHeight), parts.Chip.Frame);
    }

    [Fact]
    public void AHiddenChipLeavesTheNumberAloneOnOneRowAndWithNoWidthLimitTheHeadIsOneRow()
    {
        var parts = Build(127, 161);
        var unbounded = ((ICrossPlatformLayout)parts.Head).CrossPlatformMeasure(double.PositiveInfinity, double.PositiveInfinity);
        parts.Chip.IsVisible = false;
        var cross = (ICrossPlatformLayout)parts.Head;

        var size = cross.CrossPlatformMeasure(120, double.PositiveInfinity);
        cross.CrossPlatformArrange(new Rect(0, 0, 120, size.Height));

        Assert.Equal(new Size(127 + 12 + 161, ChipHeight), unbounded);
        Assert.False(parts.Head.IsTwoRows);
        Assert.Equal(NumberLine, size.Height, 3);
        Same(new Rect(0, 0, 127, NumberLine), parts.Number.Frame);
    }

    [Fact]
    public void TheCardHeadIsACardHeadLayoutOfTheOneLineNumberThenTheChip()
    {
        var card = Load("Shared", "Controls", "JobCard.xaml");
        var chip = Load("Shared", "Controls", "StatusChip.xaml").Root!;
        var summary = card.Descendants().Single(element => element.Attribute(_xaml + "Name")?.Value == "Summary");
        var head = summary.Elements().First();
        var children = head.Elements().ToList();
        var number = children[CardHeadLayout.NumberIndex];
        var status = children[CardHeadLayout.ChipIndex];

        Assert.Equal(_controls + "CardHeadLayout", head.Name);
        Assert.Empty(head.Attributes());
        Assert.Equal(2, children.Count);
        Assert.Equal(_maui + "Label", number.Name);
        Assert.Equal("{Binding JobNumber}", number.Attribute("Text")?.Value);
        Assert.Equal("{StaticResource FsTextMono}", number.Attribute("Style")?.Value);
        Assert.Equal("NoWrap", number.Attribute("LineBreakMode")?.Value);
        Assert.Equal("Center", number.Attribute("VerticalOptions")?.Value);
        Assert.Equal(_controls + "StatusChip", status.Name);
        Assert.Equal("{Binding Status}", status.Attribute("Status")?.Value);
        Assert.Equal("{Binding IsCompact}", status.Attribute("IsCompact")?.Value);
        Assert.All(children, child => Assert.DoesNotContain(child.Attributes(), attribute => attribute.Name.LocalName.StartsWith("Grid.", StringComparison.Ordinal)));
        Assert.Equal("Start", chip.Attribute("HorizontalOptions")?.Value);
        Assert.Equal("Center", chip.Attribute("VerticalOptions")?.Value);
        Assert.DoesNotContain(card.Descendants(_maui + "Grid"), grid => grid.Elements(_maui + "Label").Any(label => label.Attribute("Text")?.Value == "{Binding JobNumber}"));
    }

    private static (CardHeadLayout Head, NoWrapText Number, FixedView Chip) Build(double numberWidth, double chipWidth)
    {
        var number = new NoWrapText(numberWidth, NumberLine) { VerticalOptions = LayoutOptions.Center };
        var chip = new FixedView(chipWidth, ChipHeight) { HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center };
        var head = new CardHeadLayout();
        head.Add(number);
        head.Add(chip);
        return (head, number, chip);
    }

    private static XDocument Load(params string[] parts) =>
        XDocument.Load(Path.Combine([RepositoryPaths.MobileApp, .. parts]));

    private static void Same(Rect expected, Rect actual)
    {
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
        Assert.Equal(expected.Width, actual.Width, 3);
        Assert.Equal(expected.Height, actual.Height, 3);
    }

    private sealed class FixedView(double width, double height) : View
    {
        public List<double> Constraints { get; } = [];

        protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
        {
            Constraints.Add(widthConstraint);
            return new Size(width, height);
        }
    }

    private sealed class NoWrapText(double naturalWidth, double lineHeight) : View
    {
        public List<double> Constraints { get; } = [];

        protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
        {
            Constraints.Add(widthConstraint);
            return new Size(Math.Min(naturalWidth, widthConstraint), lineHeight);
        }
    }
}
