using Microsoft.Maui.Layouts;

namespace FieldService.Mobile.Shared.Controls;

public sealed class SegmentRowLayout : Layout
{
    public const double GroupInset = 8;
    public const double SegmentGap = 4;

    public bool IsStacked { get; private set; }

    public static double SegmentWidth(double contentWidth) => Math.Max(0, (contentWidth - SegmentGap) / 2);

    public static bool Stacks(double segmentWidth, double widestGroupWidth) => widestGroupWidth + (2 * GroupInset) > segmentWidth;

    public static bool StacksRow(double contentWidth, IReadOnlyCollection<double> groupWidths)
    {
        ArgumentNullException.ThrowIfNull(groupWidths);

        return groupWidths.Count > 1
            && !double.IsPositiveInfinity(contentWidth)
            && Stacks(SegmentWidth(contentWidth), groupWidths.Max());
    }

    public static IReadOnlyList<Rect> Place(Rect bounds, IReadOnlyList<double> heights, bool stacked)
    {
        ArgumentNullException.ThrowIfNull(heights);

        if (heights.Count == 1)
        {
            return [new Rect(bounds.Left, bounds.Top, bounds.Width, RowHeight(bounds, heights))];
        }

        if (stacked)
        {
            var frames = new List<Rect>(heights.Count);
            var top = bounds.Top;
            foreach (var height in heights)
            {
                frames.Add(new Rect(bounds.Left, top, bounds.Width, height));
                top += height + SegmentGap;
            }

            return frames;
        }

        var width = SegmentWidth(bounds.Width);
        var rowHeight = RowHeight(bounds, heights);
        return [.. heights.Select((_, index) => new Rect(bounds.Left + (index * (width + SegmentGap)), bounds.Top, width, rowHeight))];
    }

    public static double RowHeight(Rect bounds, IReadOnlyList<double> heights)
    {
        ArgumentNullException.ThrowIfNull(heights);

        var tallest = heights.Count == 0 ? 0 : heights.Max();
        return double.IsFinite(bounds.Height) ? Math.Max(tallest, bounds.Height) : tallest;
    }

    public static IView GroupOf(IView segment) =>
        segment is Microsoft.Maui.ILayout cell && cell.FirstOrDefault(child => child is Microsoft.Maui.ILayout) is { } group ? group : segment;

    protected override ILayoutManager CreateLayoutManager() => new Manager(this);

    private sealed class Manager(SegmentRowLayout row) : LayoutManager(row)
    {
        private readonly RemeasureRequest _remeasure = new();

        public override Size Measure(double widthConstraint, double heightConstraint)
        {
            var segments = Visible();
            if (segments.Count == 0)
            {
                return Size.Zero;
            }

            if (double.IsPositiveInfinity(widthConstraint))
            {
                row.IsStacked = false;
                var sizes = segments.Select(segment => segment.Measure(double.PositiveInfinity, double.PositiveInfinity)).ToList();
                return new Size((sizes.Max(size => size.Width) * sizes.Count) + (SegmentGap * (sizes.Count - 1)), sizes.Max(size => size.Height));
            }

            row.IsStacked = Decide(widthConstraint, segments);
            var heights = MeasureSegments(widthConstraint, segments, row.IsStacked);
            var frames = Place(new Rect(0, 0, widthConstraint, double.PositiveInfinity), heights, row.IsStacked);
            return new Size(widthConstraint, frames.Max(frame => frame.Bottom));
        }

        public override Size ArrangeChildren(Rect bounds)
        {
            var segments = Visible();
            if (segments.Count == 0)
            {
                return bounds.Size;
            }

            var stacked = row.IsStacked;
            if (_remeasure.AsksAt(bounds.Width, stacked, Decide(bounds.Width, segments)))
            {
                ((IView)row).InvalidateMeasure();
            }

            var heights = MeasureSegments(bounds.Width, segments, stacked);
            var frames = Place(bounds, heights, stacked);
            for (var index = 0; index < segments.Count; index++)
            {
                segments[index].Arrange(frames[index]);
            }

            return bounds.Size;
        }

        private static bool Decide(double width, IReadOnlyList<IView> segments) =>
            StacksRow(width, [.. segments.Select(segment => GroupOf(segment).Measure(double.PositiveInfinity, double.PositiveInfinity).Width)]);

        private static List<double> MeasureSegments(double width, IReadOnlyList<IView> segments, bool stacked)
        {
            var segmentWidth = stacked || segments.Count == 1 ? width : SegmentWidth(width);
            return [.. segments.Select(segment => segment.Measure(segmentWidth, double.PositiveInfinity).Height)];
        }

        private List<IView> Visible() => [.. row.Where(child => child.Visibility != Visibility.Collapsed)];
    }
}
