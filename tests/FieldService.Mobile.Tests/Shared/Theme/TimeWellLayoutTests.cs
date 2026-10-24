using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class TimeWellLayoutTests
{
    private const double WellAt320 = 270.095;
    private const double WellAt411 = 361.524;
    private const double Line = 26.743;
    private const double PillHeight = 28;
    private const double DatedTimeAt13 = 296;
    private const double WindowAt10 = 108;
    private const double UrgentBadge = 96;
    private const double LowPill = 60.095;
    private const double ShortPill = 80;
    private const double OneRowJustFits = 294;
    private const double HalfNarrower = OneRowJustFits - 0.5;
    private const double OnePixelNarrower = OneRowJustFits - 0.2963;

    private static readonly Thickness _oneRow = new(14, 0, 12, 0);
    private static readonly Thickness _twoRows = new(14, 10, 12, 10);

    [Fact]
    public void AStandInLabelMeasuredWithNoLimitEndsTwoLinesTallAt190ThroughTheNewMeasureItsArrangeAsksFor()
    {
        var parts = Build(DatedTimeAt13, LowPill);
        var passes = new LayoutPasses(parts.Well);

        passes.Measure(double.PositiveInfinity);
        var oneLine = parts.Label.DesiredSize.Height;
        passes.Arrange(WellAt320);
        var (keptRows, asked) = (parts.Well.IsTwoRows, passes.Invalidations);
        passes.Measure(WellAt320);
        passes.Arrange(WellAt320);

        Assert.Equal(Line, oneLine, 3);
        Assert.False(keptRows);
        Assert.Equal(1, asked);
        Assert.True(parts.Well.IsTwoRows);
        Assert.Equal(190.095, parts.Label.Constraints[^1], 3);
        Assert.Equal(2 * Line, parts.Label.DesiredSize.Height, 3);
        Same(new Rect(24, 0, 190.095, 2 * Line), parts.Label.Frame);
        Same(new Rect(14, 10, 214.095, 2 * Line), parts.Time.Frame);
        Assert.Equal(1, passes.Invalidations);
    }

    [Fact]
    public void MeasuredAtItsWidthTheWellGrowsToHoldTheWrappedTimeAndThePriorityUnderIt()
    {
        var parts = Build(DatedTimeAt13, LowPill);
        var cross = (ICrossPlatformLayout)parts.Well;

        var size = cross.CrossPlatformMeasure(WellAt320, double.PositiveInfinity);
        var measuredLast = parts.Label.Constraints[^1];
        cross.CrossPlatformArrange(new Rect(0, 0, WellAt320, size.Height));

        Assert.True(parts.Well.IsTwoRows);
        Assert.Equal(190.095, measuredLast, 3);
        Assert.Equal(WellAt320, size.Width, 3);
        Assert.Equal(10 + (2 * Line) + 4 + PillHeight + 10, size.Height, 3);
        Same(new Rect(38, 10 + (2 * Line) + 4, LowPill, PillHeight), parts.Priority.Frame);
        Same(new Rect(WellAt320 - 12 - 20, 10 + (((2 * Line) + 4 + PillHeight - 20) / 2), 20, 20), parts.Chevron.Frame);
    }

    [Fact]
    public void TheModeFlipsBothWaysThroughANewMeasureWhenTheWidthChanges()
    {
        var parts = Build(WindowAt10, UrgentBadge);
        var passes = new LayoutPasses(parts.Well);

        var wide = passes.Measure(WellAt411);
        passes.Arrange(WellAt411);
        var (wideRows, widePriority) = (parts.Well.IsTwoRows, parts.Priority.Frame);
        var narrow = passes.Measure(WellAt320);
        passes.Arrange(WellAt320);
        var (narrowRows, narrowPriority) = (parts.Well.IsTwoRows, parts.Priority.Frame);
        passes.Measure(WellAt411);
        passes.Arrange(WellAt411);

        Assert.False(wideRows);
        Same(new Rect(223.524, 0, UrgentBadge, PillHeight), widePriority);
        Assert.Equal(PillHeight, wide.Height, 3);
        Assert.True(narrowRows);
        Same(new Rect(38, 10 + Line + 4, UrgentBadge, PillHeight), narrowPriority);
        Assert.Equal(10 + Line + 4 + PillHeight + 10, narrow.Height, 3);
        Assert.False(parts.Well.IsTwoRows);
        Same(new Rect(14, (PillHeight - Line) / 2, 199.524, Line), parts.Time.Frame);
        Assert.Equal(0, passes.Invalidations);
    }

    [Fact]
    public void ArrangedHalfADpNarrowerThanItsOneRowMeasureTheWellKeepsOneRowInsideItsBoundsAndBothGapsPayTheShortfall()
    {
        var parts = Build(WindowAt10, UrgentBadge);
        var passes = new LayoutPasses(parts.Well);

        passes.Measure(OneRowJustFits);
        passes.Arrange(OneRowJustFits);
        var steady = passes.Invalidations;
        var bounds = passes.Arrange(HalfNarrower);
        var once = passes.Invalidations;
        passes.Arrange(HalfNarrower);

        Assert.False(JobCard.WellNeedsTwoRows(JobCard.TimeGroupWidth(WindowAt10), UrgentBadge, OneRowJustFits - 26));
        Assert.True(JobCard.WellNeedsTwoRows(JobCard.TimeGroupWidth(WindowAt10), UrgentBadge, HalfNarrower - 26));
        Assert.False(parts.Well.IsTwoRows);
        Assert.Equal(PillHeight, bounds.Height, 3);
        Same(new Rect(14, (PillHeight - Line) / 2, 132, Line), parts.Time.Frame);
        Same(new Rect(14 + 132 + 9.75, 0, UrgentBadge, PillHeight), parts.Priority.Frame);
        Same(new Rect(HalfNarrower - 12 - 20, (PillHeight - 20) / 2, 20, 20), parts.Chevron.Frame);
        Assert.Equal(WindowAt10, parts.Label.Constraints[^1], 3);
        Assert.Equal(Line, parts.Label.DesiredSize.Height, 3);
        foreach (var (frame, name) in new[] { (parts.Time.Frame, "time"), (parts.Priority.Frame, "priority"), (parts.Chevron.Frame, "chevron") })
        {
            LayoutPasses.Inside(bounds, frame, name);
        }

        Assert.Equal(0, steady);
        Assert.Equal(1, once);
        Assert.Equal(1, passes.Invalidations);
    }

    [Fact]
    public void MeasuredAgainAtTheArrangedWidthTheWellTakesTwoRowsAndArrangesThem()
    {
        var parts = Build(WindowAt10, UrgentBadge);
        var passes = new LayoutPasses(parts.Well);

        passes.Measure(OneRowJustFits);
        passes.Arrange(HalfNarrower);
        var size = passes.Measure(HalfNarrower);
        var bounds = passes.Arrange(HalfNarrower);

        Assert.True(parts.Well.IsTwoRows);
        Assert.Equal(10 + Line + 4 + PillHeight + 10, size.Height, 3);
        Same(new Rect(38, 10 + Line + 4, UrgentBadge, PillHeight), parts.Priority.Frame);
        LayoutPasses.Inside(bounds, parts.Priority.Frame, "priority");
        Assert.Equal(1, passes.Invalidations);
    }

    [Fact]
    public void MeasuredAt411AndArrangedAt320WithoutANewMeasureTheWellsFramesStayInsideTheBoundsAndOneInvalidationFollows()
    {
        var parts = Build(WindowAt10, ShortPill);
        var passes = new LayoutPasses(parts.Well);

        passes.Measure(WellAt411);
        var bounds = passes.Arrange(WellAt320);
        passes.Arrange(WellAt320);

        Assert.True(JobCard.WellNeedsTwoRows(JobCard.TimeGroupWidth(WindowAt10), ShortPill, WellAt320 - 26));
        Assert.False(parts.Well.IsTwoRows);
        foreach (var (frame, name) in new[] { (parts.Time.Frame, "time"), (parts.Priority.Frame, "priority"), (parts.Chevron.Frame, "chevron") })
        {
            LayoutPasses.Inside(bounds, frame, name);
        }

        Assert.True(parts.Time.Frame.Right <= parts.Priority.Frame.Left && parts.Priority.Frame.Right <= parts.Chevron.Frame.Left, $"{parts.Time.Frame} {parts.Priority.Frame} {parts.Chevron.Frame}");
        Assert.Equal(132, parts.Time.Frame.Width, 3);
        Assert.Equal(1, passes.Invalidations);
    }

    [Fact]
    public void ArrangedWiderThanItsTwoRowMeasureTheWellKeepsTwoRowsInsideItsBounds()
    {
        var parts = Build(WindowAt10, UrgentBadge);
        var passes = new LayoutPasses(parts.Well);

        passes.Measure(HalfNarrower);
        var bounds = passes.Arrange(OneRowJustFits);

        Assert.True(parts.Well.IsTwoRows);
        foreach (var (frame, name) in new[] { (parts.Time.Frame, "time"), (parts.Priority.Frame, "priority"), (parts.Chevron.Frame, "chevron") })
        {
            LayoutPasses.Inside(bounds, frame, name);
        }

        Same(new Rect(38, 10 + Line + 4, UrgentBadge, PillHeight), parts.Priority.Frame);
        Assert.Equal(1, passes.Invalidations);
    }

    [Fact]
    public void AParentThatMeasuresOnePixelWiderThanItArrangesCannotMakeTheWellFlipBackAndForth()
    {
        var parts = Build(WindowAt10, UrgentBadge);
        var passes = new LayoutPasses(parts.Well);

        var modes = passes.RunParent(OneRowJustFits, OnePixelNarrower, rounds: 10, () => parts.Well.IsTwoRows);

        Assert.True(JobCard.WellNeedsTwoRows(JobCard.TimeGroupWidth(WindowAt10), UrgentBadge, OnePixelNarrower - 26));
        Assert.All(modes, Assert.False);
        Assert.Equal(1, passes.Invalidations);
        Assert.Equal(2, passes.Measures);
        Assert.Equal(Line, parts.Label.DesiredSize.Height, 3);
    }

    [Fact]
    public void OneRowNeverGivesTheTimeLessThanItsNaturalWidthAndTakesTheShortfallFromBothGaps()
    {
        var content = new Rect(14, 0, HalfNarrower - 26, double.PositiveInfinity);

        var frames = TimeWellLayout.Place(content, new Size(132, Line), new Size(UrgentBadge, PillHeight), new Size(20, 20), twoRows: false);

        Same(new Rect(14, 0, 132, PillHeight), frames.Time);
        Same(new Rect(14 + 132 + 9.75, 0, UrgentBadge, PillHeight), frames.Priority);
        Same(new Rect(14 + HalfNarrower - 26 - 20, 0, 20, PillHeight), frames.Chevron);
        Assert.Equal(9.75, frames.Chevron.Left - frames.Priority.Right, 3);
        Assert.Equal(132 + 9.75, TimeWellLayout.PriorityLeft(HalfNarrower - 26, 132, UrgentBadge, 20), 3);
        Assert.Equal(235.429 + 10, TimeWellLayout.PriorityLeft(WellAt411 - 26, 150, LowPill, 20), 3);
        Assert.Equal(400, TimeWellLayout.PriorityLeft(100, 400, UrgentBadge, 20), 3);
    }

    [Fact]
    public void WithNoWidthLimitTheWellMeasuresAsOneRowOfItsNaturalParts()
    {
        var parts = Build(DatedTimeAt13, LowPill);

        var size = ((ICrossPlatformLayout)parts.Well).CrossPlatformMeasure(double.PositiveInfinity, double.PositiveInfinity);

        Assert.False(parts.Well.IsTwoRows);
        Assert.Equal(26 + JobCard.TimeGroupWidth(DatedTimeAt13) + LowPill + 20 + 20, size.Width, 3);
        Assert.Equal(PillHeight, size.Height, 3);
    }

    [Fact]
    public void TwoRowsGiveTheTimeTheRowUpToTheChevronAndIndentThePriorityUnderTheText()
    {
        var frames = TimeWellLayout.Place(new Rect(14, 10, WellAt411 - 26, double.PositiveInfinity), new Size(305.524, 54.095), new Size(LowPill, PillHeight), new Size(20, 20), twoRows: true);

        Same(new Rect(14, 10, 305.524, 54.095), frames.Time);
        Same(new Rect(38, 68.095, 271.524, PillHeight), frames.Priority);
        Same(new Rect(329.524, 10, 20, 86.095), frames.Chevron);
        Assert.Equal(86.095, frames.Height, 3);
        Assert.Equal(281.524, frames.Time.Width - JobCard.ClockWidth - JobCard.ClockGap, 3);
    }

    [Fact]
    public void OneRowPutsTheTimeThePriorityAndTheChevronSideBySideTenApart()
    {
        var frames = TimeWellLayout.Place(new Rect(14, 0, WellAt411 - 26, double.PositiveInfinity), new Size(150, 20.571), new Size(LowPill, PillHeight), new Size(20, 20), twoRows: false);

        Same(new Rect(14, 0, 235.429, PillHeight), frames.Time);
        Same(new Rect(259.429, 0, LowPill, PillHeight), frames.Priority);
        Same(new Rect(329.524, 0, 20, PillHeight), frames.Chevron);
        Assert.Equal(PillHeight, frames.Height, 3);
        Assert.Equal(211.429, frames.Time.Width - JobCard.ClockWidth - JobCard.ClockGap, 3);
    }

    [Theory]
    [InlineData(244.095, 60, 100, false, 144.095, 204.095)]
    [InlineData(244.095, 96, 100, false, 108.095, 204.095)]
    [InlineData(244.095, 96, 132, false, 132, 204.095)]
    [InlineData(244.095, 60, 300, true, 214.095, 180.095)]
    [InlineData(335.524, 96, 300, true, 305.524, 271.524)]
    [InlineData(30, 96, 0, false, 0, 0)]
    public void EachChildIsMeasuredWithTheRoomOfTheChosenModeAndOneRowNeverGivesTheTimeLessThanItsNaturalWidth(double content, double priority, double time, bool twoRows, double timeRoom, double priorityRoom)
    {
        Assert.Equal(timeRoom, TimeWellLayout.TimeRoom(content, priority, JobCard.ChevronWidth, time, twoRows), 3);
        Assert.Equal(priorityRoom, TimeWellLayout.PriorityRoom(content, JobCard.ChevronWidth, twoRows), 3);
    }

    [Fact]
    public void TheWellsPaddingsAreTheTokensWellRowsForNames()
    {
        var well = new TimeWellLayout();
        var host = new ContentView
        {
            Resources = new ResourceDictionary
            {
                { JobCard.WellRowsFor(twoRows: false).PaddingKey, _oneRow },
                { JobCard.WellRowsFor(twoRows: true).PaddingKey, _twoRows },
            },
            Content = well,
        };

        Assert.Same(well, host.Content);
        Assert.Equal(_oneRow, well.OneRowPadding);
        Assert.Equal(_twoRows, well.TwoRowPadding);
        Assert.Equal(_oneRow, well.PaddingFor(twoRows: false));
        Assert.Equal(_twoRows, well.PaddingFor(twoRows: true));
    }

    [Fact]
    public void AHiddenPriorityIsNeitherMeasuredNorArrangedAndLeavesItsColumnToTheTime()
    {
        var parts = Build(WindowAt10, UrgentBadge);
        parts.Priority.IsVisible = false;
        var cross = (ICrossPlatformLayout)parts.Well;

        var size = cross.CrossPlatformMeasure(WellAt320, double.PositiveInfinity);
        cross.CrossPlatformArrange(new Rect(0, 0, WellAt320, size.Height));

        Assert.False(parts.Well.IsTwoRows);
        Assert.Empty(parts.Priority.Constraints);
        Same(new Rect(14, 0, 204.095, Line), parts.Time.Frame);
    }

    private static (TimeWellLayout Well, Grid Time, WrappingText Label, FixedView Priority, FixedView Chevron) Build(double textWidth, double priorityWidth)
    {
        var label = new WrappingText(textWidth) { VerticalOptions = LayoutOptions.Center };
        var time = new LaidOutGrid { ColumnSpacing = JobCard.ClockGap, VerticalOptions = LayoutOptions.Center };
        time.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        time.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        time.Add(new FixedView(JobCard.ClockWidth, JobCard.ClockWidth) { VerticalOptions = LayoutOptions.Start }, 0, 0);
        time.Add(label, 1, 0);
        var priority = new FixedView(priorityWidth, PillHeight) { HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center };
        var chevron = new FixedView(JobCard.ChevronWidth, JobCard.ChevronWidth) { VerticalOptions = LayoutOptions.Center };
        var well = new TimeWellLayout { OneRowPadding = _oneRow, TwoRowPadding = _twoRows };
        well.Add(time);
        well.Add(priority);
        well.Add(chevron);
        return (well, time, label, priority, chevron);
    }

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

    private sealed class LaidOutGrid : Grid
    {
        protected override Size MeasureOverride(double widthConstraint, double heightConstraint) =>
            ((ICrossPlatformLayout)this).CrossPlatformMeasure(widthConstraint, heightConstraint);

        protected override Size ArrangeOverride(Rect bounds)
        {
            var size = base.ArrangeOverride(bounds);
            ((ICrossPlatformLayout)this).CrossPlatformArrange(new Rect(Point.Zero, size));
            return size;
        }
    }

    private sealed class WrappingText(double naturalWidth) : View
    {
        public List<double> Constraints { get; } = [];

        protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
        {
            Constraints.Add(widthConstraint);
            return widthConstraint >= naturalWidth
                ? new Size(naturalWidth, Line)
                : new Size(widthConstraint, Math.Ceiling(naturalWidth / Math.Max(1, widthConstraint)) * Line);
        }
    }
}
