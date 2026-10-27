using Microsoft.Maui.Layouts;

namespace FieldService.Mobile.Shared.Controls;

public sealed class DockButtonRow : Layout
{
    public const double GroupInset = 8;
    public const double ButtonGap = 10;

    public bool IsStacked { get; private set; }

    public static double ButtonWidth(double rowWidth) => Math.Max(0, (rowWidth - ButtonGap) / 2);

    public static bool Stacks(double buttonWidth, double widestGroupWidth) => widestGroupWidth + (2 * GroupInset) > buttonWidth;

    public static bool StacksRow(double rowWidth, IReadOnlyCollection<double> groupWidths)
    {
        ArgumentNullException.ThrowIfNull(groupWidths);

        return groupWidths.Count > 1
            && !double.IsPositiveInfinity(rowWidth)
            && Stacks(ButtonWidth(rowWidth), groupWidths.Max());
    }

    public static IReadOnlyList<Rect> Place(Rect bounds, IReadOnlyList<double> heights, bool stacked)
    {
        ArgumentNullException.ThrowIfNull(heights);

        if (heights.Count == 1)
        {
            return [new Rect(bounds.Left, bounds.Top, bounds.Width, heights[0])];
        }

        if (stacked)
        {
            var frames = new List<Rect>(heights.Count);
            var top = bounds.Top;
            foreach (var height in heights)
            {
                frames.Add(new Rect(bounds.Left, top, bounds.Width, height));
                top += height + ButtonGap;
            }

            return frames;
        }

        var width = ButtonWidth(bounds.Width);
        var rowHeight = heights.Count == 0 ? 0 : heights.Max();
        return [.. heights.Select((_, index) => new Rect(bounds.Left + (index * (width + ButtonGap)), bounds.Top, width, rowHeight))];
    }

    protected override ILayoutManager CreateLayoutManager() => new Manager(this);

    public static IView GroupOf(IView button) =>
        button is Microsoft.Maui.ILayout layout && layout.Count > 0 ? layout[^1] : button;

    private sealed class Manager(DockButtonRow row) : LayoutManager(row)
    {
        private readonly RemeasureRequest _remeasure = new();
        private List<double> _groupWidths = [];
        private double _measuredWidth = double.NaN;

        public override Size Measure(double widthConstraint, double heightConstraint)
        {
            var buttons = Visible();
            _measuredWidth = double.NaN;
            if (buttons.Count == 0)
            {
                _groupWidths = [];
                return Size.Zero;
            }

            var sizes = buttons.Select(button => button.Measure(double.PositiveInfinity, double.PositiveInfinity)).ToList();
            _groupWidths = [.. buttons.Select(button => GroupOf(button).DesiredSize.Width)];
            if (double.IsPositiveInfinity(widthConstraint))
            {
                row.IsStacked = false;
                return new Size(sizes.Sum(size => size.Width) + (ButtonGap * (sizes.Count - 1)), sizes.Max(size => size.Height));
            }

            row.IsStacked = StacksRow(widthConstraint, _groupWidths);
            var heights = MeasureButtons(widthConstraint, buttons, row.IsStacked, measure: true);
            var frames = Place(new Rect(0, 0, widthConstraint, double.PositiveInfinity), heights, row.IsStacked);
            _measuredWidth = widthConstraint;
            return new Size(widthConstraint, frames.Max(frame => frame.Bottom));
        }

        public override Size ArrangeChildren(Rect bounds)
        {
            var buttons = Visible();
            if (buttons.Count == 0)
            {
                return bounds.Size;
            }

            var stacked = row.IsStacked;
            if (_groupWidths.Count == buttons.Count && _remeasure.AsksAt(bounds.Width, stacked, StacksRow(bounds.Width, _groupWidths)))
            {
                ((IView)row).InvalidateMeasure();
            }

            var heights = MeasureButtons(bounds.Width, buttons, stacked, measure: !RemeasureRequest.IsMeasuredWidth(_measuredWidth, bounds.Width));
            var frames = Place(bounds, heights, stacked);
            for (var index = 0; index < buttons.Count; index++)
            {
                buttons[index].Arrange(frames[index]);
            }

            return bounds.Size;
        }

        private static List<double> MeasureButtons(double width, IReadOnlyList<IView> buttons, bool stacked, bool measure)
        {
            var buttonWidth = stacked || buttons.Count == 1 ? width : ButtonWidth(width);
            return [.. buttons.Select(button => measure ? button.Measure(buttonWidth, double.PositiveInfinity).Height : button.DesiredSize.Height)];
        }

        private List<IView> Visible() => [.. row.Where(child => child.Visibility != Visibility.Collapsed)];
    }
}
