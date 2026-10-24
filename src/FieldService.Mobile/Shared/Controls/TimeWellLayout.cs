using Microsoft.Maui.Layouts;

namespace FieldService.Mobile.Shared.Controls;

public sealed class TimeWellLayout : Layout
{
    public const int TimeIndex = 0;
    public const int PriorityIndex = 1;
    public const int ChevronIndex = 2;

    public static readonly BindableProperty OneRowPaddingProperty = BindableProperty.Create(
        nameof(OneRowPadding),
        typeof(Thickness),
        typeof(TimeWellLayout),
        default(Thickness),
        propertyChanged: (bindable, _, _) => ((IView)bindable).InvalidateMeasure());

    public static readonly BindableProperty TwoRowPaddingProperty = BindableProperty.Create(
        nameof(TwoRowPadding),
        typeof(Thickness),
        typeof(TimeWellLayout),
        default(Thickness),
        propertyChanged: (bindable, _, _) => ((IView)bindable).InvalidateMeasure());

    public TimeWellLayout()
    {
        SetDynamicResource(OneRowPaddingProperty, JobCard.WellRowsFor(twoRows: false).PaddingKey);
        SetDynamicResource(TwoRowPaddingProperty, JobCard.WellRowsFor(twoRows: true).PaddingKey);
    }

    public Thickness OneRowPadding
    {
        get => (Thickness)GetValue(OneRowPaddingProperty);
        set => SetValue(OneRowPaddingProperty, value);
    }

    public Thickness TwoRowPadding
    {
        get => (Thickness)GetValue(TwoRowPaddingProperty);
        set => SetValue(TwoRowPaddingProperty, value);
    }

    public bool IsTwoRows { get; private set; }

    public Thickness PaddingFor(bool twoRows) => twoRows ? TwoRowPadding : OneRowPadding;

    public static double PriorityRoom(double contentWidth, double chevronWidth, bool twoRows) =>
        Math.Max(0, FirstColumn(contentWidth, 0, chevronWidth) - JobCard.WellRowsFor(twoRows).PriorityIndent);

    public static double TimeRoom(double contentWidth, double priorityWidth, double chevronWidth, double timeWidth, bool twoRows)
    {
        var rows = JobCard.WellRowsFor(twoRows);
        var priorityColumn = rows.PriorityColumnIsAuto ? priorityWidth : 0;
        var first = FirstColumn(contentWidth, priorityColumn, chevronWidth);
        return rows.TimeColumnSpan == 2 ? first + JobCard.WellGap + priorityColumn : Math.Max(timeWidth, first);
    }

    public static double PriorityLeft(double contentWidth, double timeRoom, double priorityWidth, double chevronWidth)
    {
        var first = FirstColumn(contentWidth, priorityWidth, chevronWidth);
        return timeRoom > first
            ? timeRoom + Math.Clamp((contentWidth - timeRoom - priorityWidth - chevronWidth) / 2, 0, JobCard.WellGap)
            : first + JobCard.WellGap;
    }

    public static WellFrames Place(Rect content, Size time, Size priority, Size chevron, bool twoRows)
    {
        var rows = JobCard.WellRowsFor(twoRows);
        var priorityColumn = rows.PriorityColumnIsAuto ? priority.Width : 0;
        var first = FirstColumn(content.Width, priorityColumn, chevron.Width);
        var timeRoom = TimeRoom(content.Width, priority.Width, chevron.Width, time.Width, twoRows);
        var priorityBeside = rows.PriorityRow == 0;
        var firstRow = priorityBeside ? Math.Max(time.Height, priority.Height) : time.Height;
        var secondRow = priorityBeside ? 0 : priority.Height;
        var height = Math.Max(firstRow + rows.RowSpacing + secondRow, chevron.Height);
        var priorityLeft = (rows.PriorityColumn == 1 ? PriorityLeft(content.Width, timeRoom, priorityColumn, chevron.Width) : 0) + rows.PriorityIndent;
        var priorityCell = rows.PriorityColumn == 1 ? priorityColumn : first;
        return new WellFrames(
            new Rect(content.X, content.Y, timeRoom, firstRow),
            new Rect(content.X + priorityLeft, content.Y + (priorityBeside ? 0 : firstRow + rows.RowSpacing), Math.Max(0, priorityCell - rows.PriorityIndent), priorityBeside ? firstRow : secondRow),
            new Rect(content.X + content.Width - chevron.Width, content.Y, chevron.Width, height),
            height);
    }

    protected override ILayoutManager CreateLayoutManager() => new Manager(this);

    private static double FirstColumn(double contentWidth, double priorityColumn, double chevronWidth) =>
        Math.Max(0, contentWidth - priorityColumn - chevronWidth - (2 * JobCard.WellGap));

    private sealed class Manager(TimeWellLayout well) : LayoutManager(well)
    {
        private readonly RemeasureRequest _remeasure = new();

        public override Size Measure(double widthConstraint, double heightConstraint)
        {
            if (double.IsPositiveInfinity(widthConstraint))
            {
                well.IsTwoRows = false;
                var padding = well.OneRowPadding;
                var time = Natural(TimeIndex);
                var priority = Natural(PriorityIndex);
                var chevron = Natural(ChevronIndex);
                return new Size(
                    padding.HorizontalThickness + time.Width + priority.Width + chevron.Width + (2 * JobCard.WellGap),
                    padding.VerticalThickness + Math.Max(time.Height, Math.Max(priority.Height, chevron.Height)));
            }

            well.IsTwoRows = Decide(widthConstraint);
            var frames = MeasureAndPlace(new Rect(0, 0, widthConstraint, double.PositiveInfinity), well.IsTwoRows);
            return new Size(widthConstraint, frames.Height + well.PaddingFor(well.IsTwoRows).VerticalThickness);
        }

        public override Size ArrangeChildren(Rect bounds)
        {
            var twoRows = well.IsTwoRows;
            if (_remeasure.AsksAt(bounds.Width, twoRows, Decide(bounds.Width)))
            {
                ((IView)well).InvalidateMeasure();
            }

            var frames = MeasureAndPlace(bounds, twoRows);
            Arrange(TimeIndex, frames.Time);
            Arrange(PriorityIndex, frames.Priority);
            Arrange(ChevronIndex, frames.Chevron);
            return bounds.Size;
        }

        private bool Decide(double width) =>
            JobCard.WellNeedsTwoRows(Natural(TimeIndex).Width, Natural(PriorityIndex).Width, width - well.OneRowPadding.HorizontalThickness);

        private WellFrames MeasureAndPlace(Rect bounds, bool twoRows)
        {
            var padding = well.PaddingFor(twoRows);
            var content = new Rect(bounds.X + padding.Left, bounds.Y + padding.Top, Math.Max(0, bounds.Width - padding.HorizontalThickness), Math.Max(0, bounds.Height - padding.VerticalThickness));
            var chevron = Fit(ChevronIndex, content.Width);
            var priority = Fit(PriorityIndex, PriorityRoom(content.Width, chevron.Width, twoRows));
            var time = Fit(TimeIndex, TimeRoom(content.Width, priority.Width, chevron.Width, Natural(TimeIndex).Width, twoRows));
            return Place(content, time, priority, chevron, twoRows);
        }

        private Size Natural(int index) => Fit(index, double.PositiveInfinity);

        private Size Fit(int index, double width) =>
            Shown(index) is { } child ? child.Measure(width, double.PositiveInfinity) : Size.Zero;

        private void Arrange(int index, Rect frame)
        {
            if (Shown(index) is { } child)
            {
                child.Arrange(frame);
            }
        }

        private IView? Shown(int index) =>
            index < well.Count && well[index].Visibility != Visibility.Collapsed ? well[index] : null;
    }
}

public sealed record WellFrames(Rect Time, Rect Priority, Rect Chevron, double Height);
