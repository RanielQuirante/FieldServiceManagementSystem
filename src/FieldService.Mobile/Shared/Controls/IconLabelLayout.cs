using Microsoft.Maui.Layouts;

namespace FieldService.Mobile.Shared.Controls;

public sealed class IconLabelLayout : Layout
{
    public static double LabelWidthFor(double width, Thickness padding, double iconWidth) =>
        double.IsPositiveInfinity(width) ? double.PositiveInfinity : Math.Max(0, width - padding.HorizontalThickness - iconWidth);

    public static (Rect Icon, Rect Label) Place(Rect bounds, Thickness padding, Size icon, Size label)
    {
        var innerWidth = Math.Max(0, bounds.Width - padding.HorizontalThickness);
        var labelWidth = Math.Min(label.Width, Math.Max(0, innerWidth - icon.Width));
        var groupHeight = Math.Max(icon.Height, label.Height);
        var left = bounds.Left + padding.Left + ((innerWidth - icon.Width - labelWidth) / 2);
        var top = bounds.Top + padding.Top + ((bounds.Height - padding.VerticalThickness - groupHeight) / 2);
        return (new Rect(left, top, icon.Width, icon.Height), new Rect(left + icon.Width, top, labelWidth, label.Height));
    }

    protected override ILayoutManager CreateLayoutManager() => new Manager(this);

    private sealed class Manager(IconLabelLayout layout) : LayoutManager(layout)
    {
        public override Size Measure(double widthConstraint, double heightConstraint)
        {
            var (icon, label) = Parts();
            var iconSize = icon?.Measure(double.PositiveInfinity, double.PositiveInfinity) ?? Size.Zero;
            var labelSize = label?.Measure(LabelWidthFor(widthConstraint, layout.Padding, iconSize.Width), double.PositiveInfinity) ?? Size.Zero;
            var width = double.IsPositiveInfinity(widthConstraint) ? iconSize.Width + labelSize.Width + layout.Padding.HorizontalThickness : widthConstraint;
            return new Size(width, Math.Max(iconSize.Height, labelSize.Height) + layout.Padding.VerticalThickness);
        }

        public override Size ArrangeChildren(Rect bounds)
        {
            var (icon, label) = Parts();
            var (iconBounds, labelBounds) = Place(bounds, layout.Padding, icon?.DesiredSize ?? Size.Zero, label?.DesiredSize ?? Size.Zero);
            icon?.Arrange(iconBounds);
            label?.Arrange(labelBounds);
            return bounds.Size;
        }

        private (IView? Icon, IView? Label) Parts()
        {
            var visible = layout.Where(child => child.Visibility != Visibility.Collapsed).ToList();
            return (visible.Count > 1 ? visible[0] : null, visible.Count > 0 ? visible[^1] : null);
        }
    }
}
