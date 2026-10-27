using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class ArrangePassMeasureTests
{
    private const double Line = 20;
    private const double WellAt320 = 270.095;
    private const double DatedTimeAt13 = 296;

    public static TheoryData<string> Layouts => ["TimeWellLayout", "CardHeadLayout", "SegmentRowLayout", "DockButtonRow"];

    [Theory]
    [MemberData(nameof(Layouts))]
    public void ArrangedAtItsMeasuredWidthALayoutWhoseContentDoesNotFitChangesNoChildsSizeAndAsksForNoNewMeasure(string name)
    {
        var watch = new ArrangeWatch();
        var (layout, width) = Build(name, watch);
        var passes = new LayoutPasses(layout);

        passes.Measure(width);
        watch.Arranging = true;
        for (var round = 0; round < 3; round++)
        {
            passes.Arrange(width);
        }

        watch.Arranging = false;

        Assert.True(watch.Wrapped, $"{name}: the stand-in content fits, so the case proves nothing");
        Assert.Equal(0, watch.SizeChangesWhileArranging);
        Assert.Equal(0, watch.MeasuresWhileArranging);
        Assert.Equal(0, passes.Invalidations);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void ArrangedAtAnotherWidthALayoutNeverResizesAChildByMeasuringItWithoutAWidthLimit(string name)
    {
        var watch = new ArrangeWatch();
        var (layout, width) = Build(name, watch);
        var passes = new LayoutPasses(layout);

        passes.Measure(width);
        watch.Arranging = true;
        passes.Arrange(width + 3);
        passes.Arrange(width - 3);
        watch.Arranging = false;

        Assert.Equal(0, watch.UnlimitedResizesWhileArranging);
    }

    [Theory]
    [InlineData(100, 100.004, true)]
    [InlineData(100, 99.996, true)]
    [InlineData(100, 100.5, false)]
    [InlineData(double.NaN, 100, false)]
    [InlineData(double.PositiveInfinity, 100, false)]
    public void OnlyAnArrangeAtTheMeasuredWidthCanReuseTheMeasuredSizes(double measured, double arranged, bool reuses) =>
        Assert.Equal(reuses, RemeasureRequest.IsMeasuredWidth(measured, arranged));

    private static (Layout Layout, double Width) Build(string name, ArrangeWatch watch) => name switch
    {
        "TimeWellLayout" => (TimeWell(watch), WellAt320),
        "CardHeadLayout" => (CardHead(watch), 60),
        "SegmentRowLayout" => (SegmentRow(watch), 100),
        "DockButtonRow" => (DockRow(watch), 100),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static TimeWellLayout TimeWell(ArrangeWatch watch)
    {
        var time = new LaidOutGrid { ColumnSpacing = JobCard.ClockGap };
        time.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        time.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        time.Add(new Watched(watch, JobCard.ClockWidth, JobCard.ClockWidth), 0, 0);
        time.Add(new Watched(watch, DatedTimeAt13, Line), 1, 0);
        var well = new TimeWellLayout { OneRowPadding = new Thickness(14, 0, 12, 0), TwoRowPadding = new Thickness(14, 10, 12, 10) };
        well.Add(time);
        well.Add(new Watched(watch, 60.095, 28) { HorizontalOptions = LayoutOptions.Start });
        well.Add(new Watched(watch, JobCard.ChevronWidth, JobCard.ChevronWidth));
        return well;
    }

    private static CardHeadLayout CardHead(ArrangeWatch watch) =>
        [new Watched(watch, 80, Line), new Watched(watch, 100, 28)];

    private static SegmentRowLayout SegmentRow(ArrangeWatch watch) =>
    [
        new LaidOutGrid { new Watched(watch, 0, 0), new LaidOutGrid { new Watched(watch, 124, Line) }, new Watched(watch, 0, 44) },
        new LaidOutGrid { new Watched(watch, 0, 0), new LaidOutGrid { new Watched(watch, 146, Line) }, new Watched(watch, 0, 44) },
    ];

    private static DockButtonRow DockRow(ArrangeWatch watch) =>
    [
        new LaidOutGrid { new Watched(watch, 0, 52), new Watched(watch, 122, Line) },
        new LaidOutGrid { new Watched(watch, 0, 52), new Watched(watch, 69.3, Line) },
    ];

    private sealed class ArrangeWatch
    {
        public bool Arranging { get; set; }

        public bool Wrapped { get; set; }

        public int MeasuresWhileArranging { get; set; }

        public int SizeChangesWhileArranging { get; set; }

        public int UnlimitedResizesWhileArranging { get; set; }
    }

    private sealed class Watched(ArrangeWatch watch, double naturalWidth, double lineHeight) : View
    {
        private Size? _last;

        protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
        {
            var size = widthConstraint >= naturalWidth
                ? new Size(naturalWidth, lineHeight)
                : new Size(widthConstraint, Math.Ceiling(naturalWidth / Math.Max(1, widthConstraint)) * lineHeight);
            if (widthConstraint < naturalWidth)
            {
                watch.Wrapped = true;
            }

            if (watch.Arranging)
            {
                watch.MeasuresWhileArranging++;
                var resized = _last is { } last && last != size;
                watch.SizeChangesWhileArranging += resized ? 1 : 0;
                watch.UnlimitedResizesWhileArranging += resized && double.IsPositiveInfinity(widthConstraint) ? 1 : 0;
            }

            _last = size;
            return size;
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
}
