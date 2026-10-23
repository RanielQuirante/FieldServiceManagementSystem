using Microsoft.Maui.Layouts;

namespace FieldService.Mobile.Shared.Controls;

public sealed class ReadingsLayout : Layout
{
    public const double ColumnGap = 10;
    public const double RowGap = 12;
    public const double MinimumFieldWidth = 140;

    public static int ColumnsFor(double width) => (width - ColumnGap) / 2 >= MinimumFieldWidth ? 2 : 1;

    protected override ILayoutManager CreateLayoutManager() => new Manager(this);

    private sealed class Manager(ReadingsLayout layout) : LayoutManager(layout)
    {
        public override Size Measure(double widthConstraint, double heightConstraint)
        {
            var children = Visible();
            var width = double.IsInfinity(widthConstraint) ? 0 : widthConstraint;
            var height = 0d;
            foreach (var row in Rows(children, ColumnsFor(width)))
            {
                var columnWidth = row.Length == 2 ? (width - ColumnGap) / 2 : width;
                var rowHeight = row.Max(child => child.Measure(columnWidth, double.PositiveInfinity).Height);
                height += rowHeight + (height > 0 ? RowGap : 0);
            }

            return new Size(width, height);
        }

        public override Size ArrangeChildren(Rect bounds)
        {
            var children = Visible();
            var top = bounds.Top;
            var first = true;
            foreach (var row in Rows(children, ColumnsFor(bounds.Width)))
            {
                top += first ? 0 : RowGap;
                first = false;
                var columnWidth = row.Length == 2 ? (bounds.Width - ColumnGap) / 2 : bounds.Width;
                var heights = row.Select(child => child.DesiredSize.Height).ToArray();
                var rowHeight = heights.Max();
                for (var index = 0; index < row.Length; index++)
                {
                    var left = bounds.Left + (index * (columnWidth + ColumnGap));
                    var childTop = top + rowHeight - heights[index];
                    row[index].Arrange(new Rect(left, childTop, columnWidth, heights[index]));
                }

                top += rowHeight;
            }

            return bounds.Size;
        }

        private static IEnumerable<IView[]> Rows(IReadOnlyList<IView> children, int columns)
        {
            for (var index = 0; index < children.Count; index += columns)
            {
                yield return [.. children.Skip(index).Take(columns)];
            }
        }

        private List<IView> Visible() => [.. layout.Where(child => child.Visibility != Visibility.Collapsed)];
    }
}
