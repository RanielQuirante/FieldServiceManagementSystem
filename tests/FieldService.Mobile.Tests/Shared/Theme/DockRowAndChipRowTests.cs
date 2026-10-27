using System.Xml.Linq;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class DockRowAndChipRowTests
{
    private const double DockRowAt320 = 286.222;
    private const double DockRowAt411 = 377.143;
    private const double HeroContentAt320 = 288;

    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2009/xaml";
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";

    [Theory]
    [InlineData(138.11, 122.11, false)]
    [InlineData(138.11, 122.12, true)]
    [InlineData(183.57, 167.57, false)]
    [InlineData(183.57, 167.58, true)]
    public void TheDockRowStacksOnlyWhenAGroupWouldComeWithin8OfItsButtonsSides(double buttonWidth, double groupWidth, bool stacks)
    {
        Assert.Equal(8, DockButtonRow.GroupInset);
        Assert.Equal(stacks, DockButtonRow.Stacks(buttonWidth, groupWidth));
    }

    [Theory]
    [InlineData(DockRowAt320, 101.4, false)]
    [InlineData(DockRowAt320, 118.0, false)]
    [InlineData(DockRowAt320, 129.0, true)]
    [InlineData(DockRowAt320, 143.2, true)]
    [InlineData(DockRowAt320, 148.3, true)]
    [InlineData(DockRowAt320, 149.8, true)]
    [InlineData(DockRowAt411, 149.8, false)]
    public void AtA320PhoneTheRowStaysTwoUpTo13AndStacksFrom15AndAt411ItNeverStacks(double rowWidth, double navigateGroup, bool stacks)
    {
        Assert.Equal(stacks, DockButtonRow.StacksRow(rowWidth, [navigateGroup, 60]));
        Assert.Equal(stacks, DockButtonRow.StacksRow(rowWidth, [60, navigateGroup]));
    }

    [Fact]
    public void OneButtonPerRowIsTheButtonsWidthLessTheGapHalvedAnd138At320()
    {
        Assert.Equal(10, DockButtonRow.ButtonGap);
        Assert.Equal(138.111, DockButtonRow.ButtonWidth(DockRowAt320), 3);
        Assert.Equal(183.5715, DockButtonRow.ButtonWidth(DockRowAt411), 4);
        Assert.Equal(0, DockButtonRow.ButtonWidth(4));
    }

    [Theory]
    [InlineData(149.8)]
    [InlineData(400)]
    public void ARowWithOnlyNavigateNeverStacksAndTakesTheFullWidth(double group)
    {
        var frames = DockButtonRow.Place(new Rect(0, 0, DockRowAt320, 52), [52], stacked: false);

        Assert.False(DockButtonRow.StacksRow(DockRowAt320, [group]));
        Assert.Equal(new Rect(0, 0, DockRowAt320, 52), Assert.Single(frames));
    }

    [Fact]
    public void TwoUpPlacesTheButtonsSideBySideTenApartAndStackedPutsNavigateAboveCallTenApartAtFullWidth()
    {
        var bounds = new Rect(16, 0, DockRowAt320, double.PositiveInfinity);

        var twoUp = DockButtonRow.Place(bounds, [52, 52], stacked: false);
        var stacked = DockButtonRow.Place(bounds, [52, 52], stacked: true);

        Assert.Equal(new Rect(16, 0, 138.111, 52), Round(twoUp[0]));
        Assert.Equal(new Rect(164.111, 0, 138.111, 52), Round(twoUp[1]));
        Assert.Equal(new Rect(16, 0, DockRowAt320, 52), stacked[0]);
        Assert.Equal(new Rect(16, 62, DockRowAt320, 52), stacked[1]);
    }

    [Fact]
    public void TheRowDecidesFromEachButtonsLastChildMeasuredWithoutAWidthLimitAndFlipsThroughANewMeasure()
    {
        var navigate = new FixedView(143.2, 52);
        var call = new FixedView(60, 52);
        var row = new DockButtonRow();
        row.Add(navigate);
        row.Add(call);
        var passes = new LayoutPasses(row);

        var narrow = passes.Measure(DockRowAt320);
        passes.Arrange(DockRowAt320);
        var (stacked, stackedNavigate, stackedCall) = (row.IsStacked, navigate.Frame, call.Frame);
        passes.Measure(DockRowAt411);
        passes.Arrange(DockRowAt411);

        Assert.Equal(114, narrow.Height, 3);
        Assert.True(stacked);
        Assert.Equal(new Rect(0, 0, DockRowAt320, 52), stackedNavigate);
        Assert.Equal(new Rect(0, 62, DockRowAt320, 52), stackedCall);
        Assert.False(row.IsStacked);
        Assert.Equal(new Rect(0, 0, 183.572, 52), Round(navigate.Frame));
        Assert.Equal(new Rect(193.572, 0, 183.572, 52), Round(call.Frame));
        Assert.Equal(183.5715, navigate.Constraints[^1], 4);
        Assert.Contains(double.PositiveInfinity, navigate.Constraints);
        Assert.Equal(0, passes.Invalidations);
    }

    [Fact]
    public void ArrangedHalfADpNarrowerThanItsTwoUpMeasureTheDockStaysTwoUpInsideItsBoundsWithEachGroupCentredAtItsOwnWidth()
    {
        var parts = BuildDock(122, 69.3);
        var passes = new LayoutPasses(parts.Row);

        passes.Measure(DockRowAt320);
        passes.Arrange(DockRowAt320);
        var steady = passes.Invalidations;
        var bounds = passes.Arrange(DockRowAt320 - 0.5);
        var once = passes.Invalidations;
        passes.Arrange(DockRowAt320 - 0.5);

        Assert.True(DockButtonRow.StacksRow(DockRowAt320 - 0.5, [122, 69.3]));
        Assert.False(parts.Row.IsStacked);
        Assert.Equal(new Rect(0, 0, 137.861, 52), Round(parts.Navigate.Frame));
        Assert.Equal(new Rect(147.861, 0, 137.861, 52), Round(parts.Call.Frame));
        Assert.Equal((137.861 - 122) / 2, parts.NavigateGroup.Frame.X, 0.0005);
        Assert.Equal(new Rect(0, 14, 122, 24), Round(parts.NavigateGroup.Frame) with { X = 0 });
        DockInside(parts, bounds);
        Assert.Equal(0, steady);
        Assert.Equal(1, once);
        Assert.Equal(1, passes.Invalidations);
    }

    [Fact]
    public void MeasuredAgainAtTheArrangedWidthTheDockStacksAndArrangesBothButtons()
    {
        var parts = BuildDock(122, 69.3);
        var passes = new LayoutPasses(parts.Row);

        passes.Measure(DockRowAt320);
        passes.Arrange(DockRowAt320 - 0.5);
        var size = passes.Measure(DockRowAt320 - 0.5);
        var bounds = passes.Arrange(DockRowAt320 - 0.5);

        Assert.True(parts.Row.IsStacked);
        Assert.Equal(114, size.Height, 3);
        Assert.Equal(new Rect(0, 62, 285.722, 52), Round(parts.Call.Frame));
        DockInside(parts, bounds);
        Assert.Equal(1, passes.Invalidations);
    }

    [Fact]
    public void MeasuredAt411AndArrangedAt320WithoutANewMeasureTheDockStaysInsideTheBoundsAndOneInvalidationFollows()
    {
        var parts = BuildDock(129, 69.3);
        var passes = new LayoutPasses(parts.Row);

        passes.Measure(DockRowAt411);
        var bounds = passes.Arrange(DockRowAt320);
        passes.Arrange(DockRowAt320);

        Assert.True(DockButtonRow.StacksRow(DockRowAt320, [129, 69.3]));
        Assert.False(parts.Row.IsStacked);
        DockInside(parts, bounds);
        Assert.True(parts.Navigate.Frame.Right <= parts.Call.Frame.Left);
        Assert.Equal(1, passes.Invalidations);
    }

    [Fact]
    public void ArrangedWiderThanItsStackedMeasureTheDockStaysStackedInsideItsBounds()
    {
        var parts = BuildDock(122, 69.3);
        var passes = new LayoutPasses(parts.Row);

        passes.Measure(DockRowAt320 - 0.5);
        var bounds = passes.Arrange(DockRowAt320);

        Assert.True(parts.Row.IsStacked);
        Assert.Equal(new Rect(0, 62, DockRowAt320, 52), Round(parts.Call.Frame));
        DockInside(parts, bounds);
        Assert.Equal(1, passes.Invalidations);
    }

    [Fact]
    public void AParentThatMeasuresOnePixelWiderThanItArrangesCannotMakeTheDockStackAndUnstack()
    {
        var parts = BuildDock(122.05, 69.3);
        var passes = new LayoutPasses(parts.Row);

        var modes = passes.RunParent(DockRowAt320, DockRowAt320 - 0.2963, rounds: 10, () => parts.Row.IsStacked);

        Assert.False(DockButtonRow.StacksRow(DockRowAt320, [122.05, 69.3]));
        Assert.True(DockButtonRow.StacksRow(DockRowAt320 - 0.2963, [122.05, 69.3]));
        Assert.All(modes, Assert.False);
        Assert.Equal(1, passes.Invalidations);
        Assert.Equal(2, passes.Measures);
    }

    [Fact]
    public void TheGroupIsTheLastChildOfEachButtonNotTheButtonItself()
    {
        var navigateGroup = new FixedView(118.0, 24);
        var callGroup = new FixedView(69.3, 24);
        var navigate = new LaidOutGrid { new FixedView(300, 52), navigateGroup };
        var call = new LaidOutGrid { new FixedView(300, 52), callGroup };
        var row = new DockButtonRow();
        row.Add(navigate);
        row.Add(call);
        var cross = (ICrossPlatformLayout)row;

        var size = cross.CrossPlatformMeasure(DockRowAt320, double.PositiveInfinity);
        cross.CrossPlatformArrange(new Rect(0, 0, DockRowAt320, size.Height));

        Assert.Same(navigateGroup, DockButtonRow.GroupOf(navigate));
        Assert.Same(callGroup, DockButtonRow.GroupOf(call));
        Assert.False(row.IsStacked);
        Assert.Contains(double.PositiveInfinity, navigateGroup.Constraints);
        Assert.Contains(double.PositiveInfinity, callGroup.Constraints);
        Assert.Equal(new Rect(148.111, 0, 138.111, size.Height), Round(call.Frame));
    }

    [Fact]
    public void AHiddenCallLeavesNavigateAloneAcrossTheRow()
    {
        var navigate = new FixedView(149.8, 52);
        var call = new FixedView(60, 52) { IsVisible = false };
        var row = new DockButtonRow();
        row.Add(navigate);
        row.Add(call);
        var cross = (ICrossPlatformLayout)row;

        var size = cross.CrossPlatformMeasure(DockRowAt320, double.PositiveInfinity);
        cross.CrossPlatformArrange(new Rect(0, 0, DockRowAt320, size.Height));

        Assert.Equal(52, size.Height, 3);
        Assert.False(row.IsStacked);
        Assert.Equal(new Rect(0, 0, DockRowAt320, 52), navigate.Frame);
    }

    [Fact]
    public void TheDockHoldsNavigateThenCallInTheRowEachWithItsGroupAsTheLastChild()
    {
        var view = Load("Features/Jobs/JobDetailsView.xaml");
        var dock = view.Descendants(_controls + "ActionDock").Single();
        var row = dock.Descendants(_controls + "DockButtonRow").Single();
        var buttons = row.Elements().ToList();

        Assert.Equal("{Binding ShowsNavigationRow}", row.Attribute("IsVisible")?.Value);
        Assert.Equal(["Grid", "Grid"], buttons.Select(button => button.Name.LocalName));
        Assert.Equal(["{Binding NavigateCommand}", "{Binding CallCommand}"], buttons.Select(button => button.Elements(_maui + "Button").Single().Attribute("Command")?.Value));
        Assert.Equal(new string?[] { null, "{Binding CanCall}" }, buttons.Select(button => button.Attribute("IsVisible")?.Value));
        Assert.All(buttons, button =>
        {
            var group = button.Elements().Last();
            Assert.Equal("HorizontalStackLayout", group.Name.LocalName);
            Assert.Equal("Center", group.Attribute("HorizontalOptions")?.Value);
            Assert.Equal("{StaticResource FsSpace10}", group.Attribute("Spacing")?.Value);
            Assert.Equal("20", group.Elements(_controls + "Icon").Single().Attribute("Size")?.Value);
            Assert.Equal("{StaticResource FsSecondaryButton}", button.Elements(_maui + "Button").Single().Attribute("Style")?.Value);
        });
        Assert.DoesNotContain(view.Descendants(), element => element.Attributes().Any(attribute => attribute.Value.Contains("NavigateColumnSpan", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheWindowsHeroActionRowIsNotADockRow()
    {
        var view = Load("Features/Jobs/JobDetailsView.xaml");
        var heroActions = view.Descendants(_controls + "WrapRowLayout").Single(element => element.Attribute("IsVisible")?.Value == "{Binding ShowsHeroActions}");

        Assert.Empty(heroActions.Descendants(_controls + "DockButtonRow"));
        Assert.Empty(heroActions.Ancestors(_controls + "DockButtonRow"));
    }

    [Theory]
    [InlineData(150, 130, HeroContentAt320, false)]
    [InlineData(150, 130.01, HeroContentAt320, true)]
    [InlineData(150, 135.04, HeroContentAt320, true)]
    [InlineData(150, 126.74, HeroContentAt320, false)]
    [InlineData(150, 134.15, HeroContentAt320, true)]
    [InlineData(150, 135.04, 379.429, false)]
    public void ThePriorityMovesUnderTheChipOnlyWhenTheTwoAndTheirGapAreWiderThanTheHeroContent(double chip, double priority, double content, bool twoRows)
    {
        Assert.Equal(8, AppHeader.ChipGap);
        Assert.Equal(twoRows, AppHeader.ChipsNeedTwoRows(chip, priority, content));
    }

    [Fact]
    public void OneChipRowKeepsThePriorityBesideTheChipAndTwoRowsPutItUnderTheChip8Below()
    {
        Assert.Equal(new ChipRows(PriorityRow: 0, PriorityColumn: 1, RowSpacing: 0), AppHeader.ChipRowsFor(twoRows: false));
        Assert.Equal(new ChipRows(PriorityRow: 1, PriorityColumn: 0, RowSpacing: 8), AppHeader.ChipRowsFor(twoRows: true));
    }

    [Fact]
    public void TheHeroChipRowIsAGridOfTheChipThenThePriorityStartAligned()
    {
        var header = Load("Shared/Controls/AppHeader.xaml");
        var row = Named(header, "ChipRow");
        var children = row.Elements().ToList();
        var chip = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Controls", "StatusChip.xaml")).Root!;
        var priority = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Controls", "PriorityLabel.xaml")).Root!;

        Assert.Equal("Grid", row.Name.LocalName);
        Assert.Equal("Auto,*", row.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("Auto,Auto", row.Attribute("RowDefinitions")?.Value);
        Assert.Equal("{StaticResource FsSpace8}", row.Attribute("ColumnSpacing")?.Value);
        Assert.Null(row.Attribute("RowSpacing"));
        Assert.Equal(["StatusChip", "PriorityLabel"], children.Select(child => child.Name.LocalName));
        Assert.Equal(["SummaryChip", "SummaryPriorityLabel"], children.Select(child => child.Attribute(_xaml + "Name")?.Value));
        Assert.Null(children[0].Attribute("Grid.Column"));
        Assert.Equal("1", children[1].Attribute("Grid.Column")?.Value);
        Assert.Equal("{Binding SummaryStatus}", children[0].Attribute("Status")?.Value);
        Assert.Equal("{Binding SummaryPriority}", children[1].Attribute("Priority")?.Value);
        Assert.Equal("Start", chip.Attribute("HorizontalOptions")?.Value);
        Assert.Equal("Start", priority.Attribute("HorizontalOptions")?.Value);
        Assert.DoesNotContain(header.Descendants(_maui + "HorizontalStackLayout"), stack => stack.Elements(_controls + "StatusChip").Any());
    }

    [Fact]
    public void TheChipModeIsSetFromTheRowWidthAndBothPillsMeasuredWithoutAWidthLimitWheneverOneChanges()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Controls", "AppHeader.xaml.cs")).ReplaceLineEndings("\n");

        Assert.Contains("ApplyChipRows(twoRows: false);", source, StringComparison.Ordinal);
        Assert.Contains("ChipRow.SizeChanged += (_, _) => UpdateChipRows();", source, StringComparison.Ordinal);
        Assert.Contains("SummaryChip.SizeChanged += (_, _) => UpdateChipRows();", source, StringComparison.Ordinal);
        Assert.Contains("SummaryPriorityLabel.SizeChanged += (_, _) => UpdateChipRows();", source, StringComparison.Ordinal);
        Assert.Contains("var chip = SummaryChip.Measure(double.PositiveInfinity, double.PositiveInfinity).Width;", source, StringComparison.Ordinal);
        Assert.Contains("var priority = SummaryPriorityLabel.Measure(double.PositiveInfinity, double.PositiveInfinity).Width;", source, StringComparison.Ordinal);
        Assert.Contains("var twoRows = ChipsNeedTwoRows(chip, priority, ChipRow.Width);", source, StringComparison.Ordinal);
        Assert.Contains("propertyChanged: (bindable, _, _) => ((AppHeader)bindable).OnSummaryStatusChanged());", source, StringComparison.Ordinal);
        Assert.Contains("propertyChanged: (bindable, _, _) => ((AppHeader)bindable).ScheduleChipRows());", source, StringComparison.Ordinal);
        Assert.Contains("        ApplyIcons();\n        ScheduleChipRows();\n", source, StringComparison.Ordinal);
    }

    private static (DockButtonRow Row, LaidOutGrid Navigate, LaidOutGrid Call, FixedView NavigateGroup, FixedView CallGroup) BuildDock(double navigateGroup, double callGroup)
    {
        var navigateLabel = new FixedView(navigateGroup, 24) { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        var callLabel = new FixedView(callGroup, 24) { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        var navigate = new LaidOutGrid { new FixedView(0, 52), navigateLabel };
        var call = new LaidOutGrid { new FixedView(0, 52), callLabel };
        var row = new DockButtonRow();
        row.Add(navigate);
        row.Add(call);
        return (row, navigate, call, navigateLabel, callLabel);
    }

    private static void DockInside((DockButtonRow Row, LaidOutGrid Navigate, LaidOutGrid Call, FixedView NavigateGroup, FixedView CallGroup) parts, Rect bounds)
    {
        LayoutPasses.Inside(bounds, parts.Navigate.Frame, "Navigate");
        LayoutPasses.Inside(bounds, parts.Call.Frame, "Call");
        LayoutPasses.Inside(new Rect(Point.Zero, parts.Navigate.Frame.Size), parts.NavigateGroup.Frame, "Navigate group");
        LayoutPasses.Inside(new Rect(Point.Zero, parts.Call.Frame.Size), parts.CallGroup.Frame, "Call group");
    }

    private static Rect Round(Rect rect) => new(Math.Round(rect.X, 3), Math.Round(rect.Y, 3), Math.Round(rect.Width, 3), Math.Round(rect.Height, 3));

    private static XElement Named(XDocument document, string name) =>
        document.Descendants().Single(element => element.Attribute(_xaml + "Name")?.Value == name);

    private static XDocument Load(string relative) =>
        XDocument.Load(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));

    private sealed class FixedView(double width, double height) : View
    {
        public List<double> Constraints { get; } = [];

        protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
        {
            Constraints.Add(widthConstraint);
            return new Size(width, height);
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
