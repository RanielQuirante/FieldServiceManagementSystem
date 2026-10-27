using Microsoft.Maui.Layouts;

namespace FieldService.Mobile.Shared.Controls;

public sealed class CardHeadLayout : Layout
{
    public const int NumberIndex = 0;
    public const int ChipIndex = 1;
    public const double ChipGap = 12;
    public const double RowGap = 8;

    public bool IsTwoRows { get; private set; }

    public static bool HeadNeedsTwoRows(double numberWidth, double chipWidth, double rowWidth) =>
        numberWidth + ChipGap + chipWidth > rowWidth;

    public static double NumberRoom(double rowWidth, double numberWidth, double chipWidth, bool twoRows) =>
        Math.Max(numberWidth, twoRows ? rowWidth : rowWidth - ChipGap - chipWidth);

    public static double ChipRoom(double rowWidth, double numberWidth, double chipWidth, bool twoRows) =>
        twoRows ? Math.Max(0, rowWidth) : Math.Max(chipWidth, rowWidth - ChipGap - numberWidth);

    public static HeadFrames Place(Rect bounds, Size number, Size chip, bool twoRows)
    {
        if (twoRows)
        {
            return new HeadFrames(
                new Rect(bounds.X, bounds.Y, NumberRoom(bounds.Width, number.Width, chip.Width, twoRows: true), number.Height),
                new Rect(bounds.X, bounds.Y + number.Height + RowGap, chip.Width, chip.Height),
                number.Height + RowGap + chip.Height);
        }

        var height = Math.Max(number.Height, chip.Height);
        return new HeadFrames(
            new Rect(bounds.X, bounds.Y, NumberRoom(bounds.Width, number.Width, chip.Width, twoRows: false), height),
            new Rect(bounds.X + bounds.Width - chip.Width, bounds.Y, chip.Width, height),
            height);
    }

    protected override ILayoutManager CreateLayoutManager() => new Manager(this);

    private sealed class Manager(CardHeadLayout head) : LayoutManager(head)
    {
        private readonly RemeasureRequest _remeasure = new();
        private Size _naturalNumber;
        private Size _naturalChip;
        private double _measuredWidth = double.NaN;

        public override Size Measure(double widthConstraint, double heightConstraint)
        {
            _naturalNumber = Natural(NumberIndex);
            _naturalChip = Natural(ChipIndex);
            if (double.IsPositiveInfinity(widthConstraint))
            {
                head.IsTwoRows = false;
                _measuredWidth = double.NaN;
                var gap = Shown(ChipIndex) is null ? 0 : ChipGap;
                return new Size(_naturalNumber.Width + gap + _naturalChip.Width, Math.Max(_naturalNumber.Height, _naturalChip.Height));
            }

            head.IsTwoRows = Decide(widthConstraint);
            var frames = MeasureAndPlace(new Rect(0, 0, widthConstraint, double.PositiveInfinity), head.IsTwoRows, measure: true);
            _measuredWidth = widthConstraint;
            return new Size(widthConstraint, frames.Height);
        }

        public override Size ArrangeChildren(Rect bounds)
        {
            var twoRows = head.IsTwoRows;
            if (_remeasure.AsksAt(bounds.Width, twoRows, Decide(bounds.Width)))
            {
                ((IView)head).InvalidateMeasure();
            }

            var frames = MeasureAndPlace(bounds, twoRows, measure: !RemeasureRequest.IsMeasuredWidth(_measuredWidth, bounds.Width));
            Arrange(NumberIndex, frames.Number);
            Arrange(ChipIndex, frames.Chip);
            return bounds.Size;
        }

        private bool Decide(double width) =>
            Shown(ChipIndex) is not null && HeadNeedsTwoRows(_naturalNumber.Width, _naturalChip.Width, width);

        private HeadFrames MeasureAndPlace(Rect bounds, bool twoRows, bool measure)
        {
            var chip = Fit(ChipIndex, ChipRoom(bounds.Width, _naturalNumber.Width, _naturalChip.Width, twoRows), measure);
            var number = Fit(NumberIndex, NumberRoom(bounds.Width, _naturalNumber.Width, chip.Width, twoRows), measure);
            return Place(bounds, new Size(Math.Max(_naturalNumber.Width, number.Width), number.Height), chip, twoRows);
        }

        private Size Natural(int index) => Fit(index, double.PositiveInfinity, measure: true);

        private Size Fit(int index, double width, bool measure) =>
            Shown(index) is { } child ? (measure ? child.Measure(width, double.PositiveInfinity) : child.DesiredSize) : Size.Zero;

        private void Arrange(int index, Rect frame)
        {
            if (Shown(index) is { } child)
            {
                child.Arrange(frame);
            }
        }

        private IView? Shown(int index) =>
            index < head.Count && head[index].Visibility != Visibility.Collapsed ? head[index] : null;
    }
}

public sealed record HeadFrames(Rect Number, Rect Chip, double Height);
