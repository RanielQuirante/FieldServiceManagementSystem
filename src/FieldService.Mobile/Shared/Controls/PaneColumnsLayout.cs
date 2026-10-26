using Microsoft.Maui.Layouts;

namespace FieldService.Mobile.Shared.Controls;

public sealed class PaneColumnsLayout : Layout
{
    public const double MinimumColumnWidth = 300;
    public const double PaneGap = 20;
    public const double PhoneGap = 16;

    public static readonly BindableProperty AllowsTwoColumnsProperty = BindableProperty.Create(
        nameof(AllowsTwoColumns),
        typeof(bool),
        typeof(PaneColumnsLayout),
        false,
        propertyChanged: (bindable, _, _) => ((IView)bindable).InvalidateMeasure());

    public bool AllowsTwoColumns
    {
        get => (bool)GetValue(AllowsTwoColumnsProperty);
        set => SetValue(AllowsTwoColumnsProperty, value);
    }

    public bool IsTwoColumns { get; private set; }

    public double Gap => GapFor(AllowsTwoColumns);

    public static double GapFor(bool allowsTwoColumns) => allowsTwoColumns ? PaneGap : PhoneGap;

    public static double TwoColumnsFrom => (2 * MinimumColumnWidth) + PaneGap;

    public static bool UsesTwoColumns(double width) => !double.IsInfinity(width) && width >= TwoColumnsFrom;

    public static double ColumnWidth(double width, double gap) => Math.Max(0, (width - gap) / 2);

    public static IReadOnlyList<Rect> Place(Rect bounds, IReadOnlyList<double> heights, bool twoColumns, double gap)
    {
        ArgumentNullException.ThrowIfNull(heights);

        if (twoColumns && heights.Count == 2)
        {
            var width = ColumnWidth(bounds.Width, gap);
            return
            [
                new Rect(bounds.Left, bounds.Top, width, heights[0]),
                new Rect(bounds.Left + width + gap, bounds.Top, width, heights[1]),
            ];
        }

        var frames = new List<Rect>(heights.Count);
        var top = bounds.Top;
        foreach (var height in heights)
        {
            frames.Add(new Rect(bounds.Left, top, bounds.Width, height));
            top += height + gap;
        }

        return frames;
    }

    protected override ILayoutManager CreateLayoutManager() => new Manager(this);

    private sealed class Manager(PaneColumnsLayout pane) : LayoutManager(pane)
    {
        private readonly RemeasureRequest _remeasure = new();

        public override Size Measure(double widthConstraint, double heightConstraint)
        {
            var children = Visible();
            if (children.Count == 0)
            {
                pane.IsTwoColumns = false;
                return Size.Zero;
            }

            pane.IsTwoColumns = Decide(widthConstraint, children);
            var width = double.IsInfinity(widthConstraint)
                ? children.Max(child => child.Measure(double.PositiveInfinity, double.PositiveInfinity).Width)
                : widthConstraint;
            var heights = MeasureChildren(width, children, pane.IsTwoColumns);
            var frames = Place(new Rect(0, 0, width, double.PositiveInfinity), heights, pane.IsTwoColumns, pane.Gap);
            return new Size(width, frames.Max(frame => frame.Bottom));
        }

        public override Size ArrangeChildren(Rect bounds)
        {
            var children = Visible();
            if (children.Count == 0)
            {
                return bounds.Size;
            }

            var twoColumns = pane.IsTwoColumns;
            if (_remeasure.AsksAt(bounds.Width, twoColumns, Decide(bounds.Width, children)))
            {
                ((IView)pane).InvalidateMeasure();
            }

            var heights = MeasureChildren(bounds.Width, children, twoColumns);
            var frames = Place(bounds, heights, twoColumns, pane.Gap);
            for (var index = 0; index < children.Count; index++)
            {
                children[index].Arrange(frames[index]);
            }

            return bounds.Size;
        }

        private bool Decide(double width, IReadOnlyList<IView> children) =>
            pane.AllowsTwoColumns && children.Count == 2 && UsesTwoColumns(width);

        private List<double> MeasureChildren(double width, IReadOnlyList<IView> children, bool twoColumns)
        {
            var childWidth = twoColumns ? ColumnWidth(width, pane.Gap) : width;
            return [.. children.Select(child => child.Measure(childWidth, double.PositiveInfinity).Height)];
        }

        private List<IView> Visible() => [.. pane.Where(child => child.Visibility != Visibility.Collapsed)];
    }
}
