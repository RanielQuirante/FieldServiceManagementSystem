using System.Xml.Linq;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class HeroActionRowTests
{
    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";
    private static readonly Thickness _padding = new(16, 0);

    [Fact]
    public void ThePaneHeroActionsWrapToANewRowAndEachButtonHugsAWrappingLabel()
    {
        var view = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Features", "Jobs", "JobDetailsView.xaml"));
        var row = view.Descendants(_controls + "WrapRowLayout").Single(element => element.Attribute("IsVisible")?.Value == "{Binding ShowsHeroActions}");
        var buttons = row.Elements(_maui + "Grid").ToList();

        Assert.Equal("True", row.Attribute(_controls + "FocusMoves.HoldsHiddenFocus")?.Value);
        Assert.Equal("{StaticResource FsSpace10}", row.Attribute("ColumnSpacing")?.Value);
        Assert.Equal("{StaticResource FsSpace10}", row.Attribute("RowSpacing")?.Value);
        Assert.Empty(row.Descendants(_maui + "HorizontalStackLayout"));
        Assert.Equal(6, buttons.Count);
        Assert.All(buttons, button =>
        {
            var overlay = button.Elements(_controls + "IconLabelLayout").Single();
            var cell = overlay.Elements(_maui + "Grid").Single();
            var label = overlay.Elements(_maui + "Label").Single();
            var box = cell.Elements(_maui + "Label").Single();
            Assert.Equal("True", overlay.Attribute("HugsContent")?.Value);
            Assert.Equal("{StaticResource FsPaddingDesktopButton}", overlay.Attribute("Padding")?.Value);
            Assert.Equal(("True", "True"), (overlay.Attribute("InputTransparent")?.Value, overlay.Attribute("AutomationProperties.ExcludedWithChildren")?.Value));
            Assert.Equal(("18", "0,0,8,0"), (cell.Attribute("WidthRequest")?.Value, cell.Attribute("Margin")?.Value));
            Assert.Equal(("A", "0", "{StaticResource FsTextButtonLabelDesktop}"), (box.Attribute("Text")?.Value, box.Attribute("Opacity")?.Value, box.Attribute("Style")?.Value));
            Assert.Equal("18", cell.Elements(_controls + "Icon").Single().Attribute("Size")?.Value);
            Assert.Equal("WordWrap", label.Attribute("LineBreakMode")?.Value);
        });
    }

    [Fact]
    public void AHuggingButtonIsAsWideAsItsIconAndOneLineLabelAndIsNotMeasuredAgainWhenArrangedAtThatWidth()
    {
        var (layout, cell, label) = Button(oneLineWidth: 92.4, lineHeight: 19.6);
        var cross = (ICrossPlatformLayout)layout;

        var measured = cross.CrossPlatformMeasure(520, double.PositiveInfinity);
        cross.CrossPlatformArrange(new Rect(0, 0, measured.Width, 48));

        Assert.Equal(26 + 92.4 + 32, measured.Width, 3);
        Assert.Single(label.Constraints);
        Assert.Equal(16, cell.Frame.X, 3);
        Assert.Equal(42, label.Frame.X, 3);
        Assert.Equal(TextAlignment.Center, label.HorizontalTextAlignment);
    }

    [Fact]
    public void AHuggingButtonWiderThanItsRowTakesTheRowAndWrapsItsLabel()
    {
        var (layout, _, label) = Button(oneLineWidth: 400, lineHeight: 44.1);
        var cross = (ICrossPlatformLayout)layout;

        var measured = cross.CrossPlatformMeasure(300, double.PositiveInfinity);
        cross.CrossPlatformArrange(new Rect(0, 0, measured.Width, measured.Height));

        Assert.Equal(300, measured.Width, 3);
        Assert.Equal(88.2, measured.Height, 3);
        Assert.Equal(TextAlignment.Start, label.HorizontalTextAlignment);
        Assert.Equal(300 - 32 - 26, label.Frame.Width, 3);
    }

    [Theory]
    [InlineData(1.0, 1)]
    [InlineData(1.75, 1)]
    [InlineData(2.0, 2)]
    [InlineData(2.25, 2)]
    public void AtLargeTextTheSecondHeroActionMovesToItsOwnRowInsteadOfRunningPastTheHero(double scale, int rows)
    {
        const double HeroContent = 520;
        var icon = new Size(26, 19.6 * scale);
        double[] widths =
        [
            IconLabelLayout.WidthFor(HeroContent, _padding, icon, new Size(132 * scale, 19.6 * scale), hugs: true),
            IconLabelLayout.WidthFor(HeroContent, _padding, icon, new Size(92.4 * scale, 19.6 * scale), hugs: true),
        ];

        var lines = WrapRowLayout.LinesFor(HeroContent, widths, 10);
        var frames = WrapRowLayout.Place(new Rect(0, 0, HeroContent, 200), [.. widths.Select(width => new Size(width, 48))], lines, 10, 10);

        Assert.Equal(rows, lines[^1] + 1);
        Assert.All(frames, frame => Assert.True(frame.Right <= HeroContent + 0.001));
        Assert.Equal(HeroContent, IconLabelLayout.WidthFor(HeroContent, _padding, icon, new Size(HeroContent, 19.6 * scale), hugs: true));
        Assert.Equal(HeroContent, IconLabelLayout.WidthFor(HeroContent, _padding, icon, new Size(92.4, 19.6), hugs: false));
    }

    [Theory]
    [InlineData(true, false, false, true, false, true)]
    [InlineData(true, true, false, true, false, false)]
    [InlineData(false, false, false, true, false, false)]
    [InlineData(true, false, true, true, false, false)]
    [InlineData(true, false, false, false, false, false)]
    [InlineData(true, false, false, true, true, false)]
    public void OnlyTheWindowsRecoveryMoveAwayFromAHiddenHeroActionIsHeldBack(bool undirected, bool appMove, bool oldShown, bool oldHeld, bool newHeld, bool held)
    {
        Assert.Equal(held, FocusMoves.HoldsRecovery(undirected, appMove, oldShown, oldHeld, newHeld));
    }

    [Fact]
    public void AFocusMoveTheAppAsksForIsMarkedOnlyWhileItRuns()
    {
        var during = false;

        var moved = FocusMoves.ByApp(() =>
        {
            during = FocusMoves.IsMoving;
            return true;
        });

        Assert.True(moved);
        Assert.True(during);
        Assert.False(FocusMoves.IsMoving);
        Assert.Throws<InvalidOperationException>(() => FocusMoves.ByApp(() => throw new InvalidOperationException()));
        Assert.False(FocusMoves.IsMoving);
        Assert.Equal(TimeSpan.FromMilliseconds(400), FocusMoves.HiddenFocusGrace);
    }

    private static (IconLabelLayout Layout, FixedView Cell, WrappingLabel Label) Button(double oneLineWidth, double lineHeight)
    {
        var cell = new FixedView(26, lineHeight);
        var label = new WrappingLabel(oneLineWidth, lineHeight);
        var layout = new IconLabelLayout { Padding = _padding, HugsContent = true };
        layout.Add(cell);
        layout.Add(label);
        return (layout, cell, label);
    }

    private sealed class FixedView(double width, double height) : View
    {
        protected override Size MeasureOverride(double widthConstraint, double heightConstraint) => new(width, height);
    }

    private sealed class WrappingLabel(double oneLineWidth, double lineHeight) : Label
    {
        public List<double> Constraints { get; } = [];

        protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
        {
            Constraints.Add(widthConstraint);
            return widthConstraint >= oneLineWidth
                ? new Size(oneLineWidth, lineHeight)
                : new Size(widthConstraint, lineHeight * 2);
        }
    }
}
