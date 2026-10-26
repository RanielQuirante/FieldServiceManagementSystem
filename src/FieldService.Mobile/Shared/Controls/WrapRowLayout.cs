using Microsoft.Maui.Layouts;

namespace FieldService.Mobile.Shared.Controls;

public sealed class WrapRowLayout : Layout
{
    public const double DefaultSpacing = 8;

    public static readonly BindableProperty ColumnSpacingProperty = BindableProperty.Create(
        nameof(ColumnSpacing),
        typeof(double),
        typeof(WrapRowLayout),
        DefaultSpacing,
        propertyChanged: (bindable, _, _) => ((IView)bindable).InvalidateMeasure());

    public static readonly BindableProperty RowSpacingProperty = BindableProperty.Create(
        nameof(RowSpacing),
        typeof(double),
        typeof(WrapRowLayout),
        DefaultSpacing,
        propertyChanged: (bindable, _, _) => ((IView)bindable).InvalidateMeasure());

    private IReadOnlyList<int> _lines = [];

    public double ColumnSpacing
    {
        get => (double)GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    public int LineCount => _lines.Count == 0 ? 0 : _lines[^1] + 1;

    public static IReadOnlyList<int> LinesFor(double width, IReadOnlyList<double> widths, double columnSpacing)
    {
        ArgumentNullException.ThrowIfNull(widths);

        var lines = new List<int>(widths.Count);
        var line = 0;
        var used = 0d;
        foreach (var itemWidth in widths)
        {
            var needed = used == 0 ? itemWidth : used + columnSpacing + itemWidth;
            if (used > 0 && !double.IsInfinity(width) && needed > width)
            {
                line++;
                needed = itemWidth;
            }

            lines.Add(line);
            used = needed;
        }

        return lines;
    }

    public static IReadOnlyList<Rect> Place(Rect bounds, IReadOnlyList<Size> sizes, IReadOnlyList<int> lines, double columnSpacing, double rowSpacing)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        ArgumentNullException.ThrowIfNull(lines);

        var frames = new Rect[sizes.Count];
        var top = bounds.Top;
        var index = 0;
        while (index < sizes.Count)
        {
            var line = lines[index];
            var end = index;
            var height = 0d;
            while (end < sizes.Count && lines[end] == line)
            {
                height = Math.Max(height, sizes[end].Height);
                end++;
            }

            var left = bounds.Left;
            for (var item = index; item < end; item++)
            {
                frames[item] = new Rect(left, top + ((height - sizes[item].Height) / 2), sizes[item].Width, sizes[item].Height);
                left += sizes[item].Width + columnSpacing;
            }

            top += height + rowSpacing;
            index = end;
        }

        return frames;
    }

    public static double HeightOf(IReadOnlyList<Rect> frames) => frames.Count == 0 ? 0 : frames.Max(frame => frame.Bottom) - frames.Min(frame => frame.Top);

    protected override ILayoutManager CreateLayoutManager() => new Manager(this);

    private sealed class Manager(WrapRowLayout row) : LayoutManager(row)
    {
        private readonly RemeasureRequest _remeasure = new();

        public override Size Measure(double widthConstraint, double heightConstraint)
        {
            var children = Visible();
            if (children.Count == 0)
            {
                row._lines = [];
                return Size.Zero;
            }

            var sizes = children.Select(child => child.Measure(widthConstraint, double.PositiveInfinity)).ToList();
            row._lines = LinesFor(widthConstraint, [.. sizes.Select(size => size.Width)], row.ColumnSpacing);
            var frames = Place(Rect.Zero, sizes, row._lines, row.ColumnSpacing, row.RowSpacing);
            var width = double.IsInfinity(widthConstraint) ? frames.Max(frame => frame.Right) : widthConstraint;
            return new Size(width, HeightOf(frames));
        }

        public override Size ArrangeChildren(Rect bounds)
        {
            var children = Visible();
            if (children.Count == 0)
            {
                return bounds.Size;
            }

            var sizes = children.Select(child => child.DesiredSize).ToList();
            var lines = row._lines.Count == children.Count ? row._lines : LinesFor(bounds.Width, [.. sizes.Select(size => size.Width)], row.ColumnSpacing);
            var atArranged = LinesFor(bounds.Width, [.. sizes.Select(size => size.Width)], row.ColumnSpacing);
            if (_remeasure.AsksAt(bounds.Width, true, lines.SequenceEqual(atArranged)))
            {
                ((IView)row).InvalidateMeasure();
            }

            var frames = Place(bounds, sizes, lines, row.ColumnSpacing, row.RowSpacing);
            for (var index = 0; index < children.Count; index++)
            {
                children[index].Arrange(frames[index]);
            }

            return bounds.Size;
        }

        private List<IView> Visible() => [.. row.Where(child => child.Visibility != Visibility.Collapsed)];
    }
}
