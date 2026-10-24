using System.Globalization;
using System.Xml.Linq;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class SegmentRowLayoutTests
{
    private const double RowAt320 = 278;
    private const double RowAt411 = 369.429;
    private const double SegmentHeight = 48;
    private const double GroupHeight = 38.476;
    private const double CompletedSemiBoldAt15 = 120.889;
    private const double CompletedMediumAt15 = 117.65;
    private const double HalfNarrower = RowAt320 - 0.5;
    private const string StaticResourcePrefix = "{StaticResource ";

    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2009/xaml";
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";

    [Theory]
    [InlineData(137, 121, false)]
    [InlineData(137, 121.01, true)]
    [InlineData(182.5, 166.5, false)]
    [InlineData(182.5, 166.51, true)]
    public void TheSegmentsStackOnlyWhenTheWiderGroupWouldComeWithin8OfItsSegmentsSides(double segmentWidth, double groupWidth, bool stacks)
    {
        Assert.Equal(8, SegmentRowLayout.GroupInset);
        Assert.Equal(stacks, SegmentRowLayout.Stacks(segmentWidth, groupWidth));
    }

    [Fact]
    public void ASegmentIsHalfTheTrackContentLessTheFourGapAndTheGapIsTheSpaceToken()
    {
        Assert.Equal(4, SegmentRowLayout.SegmentGap);
        Assert.Equal(SegmentRowLayout.SegmentGap, Number(Resolve("{StaticResource FsSpace4}")));
        Assert.Equal(137, SegmentRowLayout.SegmentWidth(RowAt320), 3);
        Assert.Equal(182.7145, SegmentRowLayout.SegmentWidth(RowAt411), 4);
        Assert.Equal(0, SegmentRowLayout.SegmentWidth(2));
    }

    [Theory]
    [InlineData(RowAt320, 143.2, false)]
    [InlineData(RowAt411, 160, false)]
    public void StacksRowNeedsTwoGroupsAndAWidthLimit(double rowWidth, double group, bool stacks)
    {
        Assert.Equal(stacks, SegmentRowLayout.StacksRow(rowWidth, [group]));
        Assert.False(SegmentRowLayout.StacksRow(double.PositiveInfinity, [group, group]));
        Assert.True(SegmentRowLayout.StacksRow(RowAt320, [group, 130]));
        Assert.Throws<ArgumentNullException>(() => SegmentRowLayout.StacksRow(RowAt320, null!));
    }

    [Fact]
    public void TwoUpPlacesTheSegmentsSideBySideFourApartAtTheTallerHeightAndStackedPutsActiveAboveCompletedFourApartAtFullWidth()
    {
        var bounds = new Rect(5, 5, RowAt320, double.PositiveInfinity);

        var twoUp = SegmentRowLayout.Place(bounds, [48, 50.5], stacked: false);
        var stacked = SegmentRowLayout.Place(bounds, [48, 50.5], stacked: true);
        var single = SegmentRowLayout.Place(bounds, [48], stacked: false);

        Assert.Equal([new Rect(5, 5, 137, 50.5), new Rect(146, 5, 137, 50.5)], twoUp);
        Assert.Equal([new Rect(5, 5, RowAt320, 48), new Rect(5, 57, RowAt320, 50.5)], stacked);
        Assert.Equal(new Rect(5, 5, RowAt320, 48), Assert.Single(single));
    }

    [Fact]
    public void TheRowDecidesFromEachGroupMeasuredWithoutAWidthLimitAndMeasuresEachSegmentAgainInTheChosenRoom()
    {
        var parts = Build(activeGroup: 124.19, completedGroup: 146.286);
        var cross = (ICrossPlatformLayout)parts.Row;

        var size = cross.CrossPlatformMeasure(RowAt320, double.PositiveInfinity);
        var groupConstraints = parts.CompletedGroup.Constraints.ToList();
        cross.CrossPlatformArrange(new Rect(0, 0, RowAt320, size.Height));

        Assert.True(parts.Row.IsStacked);
        Assert.Equal(SegmentHeight + 4 + SegmentHeight, size.Height, 3);
        Assert.Equal(RowAt320, size.Width, 3);
        Assert.Equal(double.PositiveInfinity, groupConstraints[0]);
        Assert.Equal(RowAt320, groupConstraints[^1], 3);
        Same(new Rect(0, 0, RowAt320, SegmentHeight), parts.Active.Frame);
        Same(new Rect(0, SegmentHeight + 4, RowAt320, SegmentHeight), parts.Completed.Frame);
        Same(new Rect((RowAt320 - 146.286) / 2, (SegmentHeight - 38.476) / 2, 146.286, 38.476), parts.CompletedGroup.Frame);
    }

    [Fact]
    public void TheModeFlipsBothWaysThroughANewMeasureWhenTheWidthChangesWithTheFramesOfEachMode()
    {
        var parts = Build(activeGroup: 124.19, completedGroup: 146.286);
        var passes = new LayoutPasses(parts.Row);

        var wide = passes.Measure(RowAt411);
        passes.Arrange(RowAt411);
        var (wideStacked, wideActive, wideCompleted) = (parts.Row.IsStacked, parts.Active.Frame, parts.Completed.Frame);
        var narrow = passes.Measure(RowAt320);
        passes.Arrange(RowAt320);
        var (narrowStacked, narrowActive, narrowCompleted) = (parts.Row.IsStacked, parts.Active.Frame, parts.Completed.Frame);
        passes.Measure(RowAt411);
        passes.Arrange(RowAt411);

        Assert.False(wideStacked);
        Assert.Equal(SegmentHeight, wide.Height, 3);
        Same(new Rect(0, 0, 182.7145, SegmentHeight), wideActive);
        Same(new Rect(186.7145, 0, 182.7145, SegmentHeight), wideCompleted);
        Assert.True(narrowStacked);
        Same(new Rect(0, 0, RowAt320, SegmentHeight), narrowActive);
        Same(new Rect(0, SegmentHeight + 4, RowAt320, SegmentHeight), narrowCompleted);
        Assert.Equal(SegmentHeight + 4 + SegmentHeight, narrow.Height, 3);
        Assert.False(parts.Row.IsStacked);
        Same(new Rect(186.7145, 0, 182.7145, SegmentHeight), parts.Completed.Frame);
        Assert.Equal(0, passes.Invalidations);
    }

    [Fact]
    public void TwoUpFillsTheArrangedHeightAsTheGridsStarRowDidAndStackedKeepsEachSegmentsOwnHeight()
    {
        var bounds = new Rect(0, 0, RowAt320, 48.074);

        var twoUp = SegmentRowLayout.Place(bounds, [48, 48], stacked: false);
        var stacked = SegmentRowLayout.Place(bounds, [48, 48], stacked: true);

        Assert.All(twoUp, frame => Assert.Equal(48.074, frame.Height, 3));
        Assert.All(stacked, frame => Assert.Equal(48, frame.Height, 3));
        Assert.Equal(48.074, SegmentRowLayout.RowHeight(bounds, [48, 48]), 3);
        Assert.Equal(50.5, SegmentRowLayout.RowHeight(new Rect(0, 0, RowAt320, double.PositiveInfinity), [48, 50.5]), 3);
        Assert.Equal(50.5, SegmentRowLayout.RowHeight(bounds, [48, 50.5]), 3);
        Assert.Equal(0, SegmentRowLayout.RowHeight(new Rect(0, 0, RowAt320, double.PositiveInfinity), []));
    }

    [Fact]
    public void ArrangedHalfADpNarrowerThanItsTwoUpMeasureTheRowStaysTwoUpInsideItsBoundsWithEachOverlayCentredAtItsOwnWidth()
    {
        var parts = Build(activeGroup: 96, completedGroup: CompletedSemiBoldAt15);
        var passes = new LayoutPasses(parts.Row);

        passes.Measure(RowAt320);
        passes.Arrange(RowAt320);
        var steady = passes.Invalidations;
        var bounds = passes.Arrange(HalfNarrower);
        var once = passes.Invalidations;
        passes.Arrange(HalfNarrower);

        Assert.True(SegmentRowLayout.StacksRow(HalfNarrower, [96, CompletedSemiBoldAt15]));
        Assert.False(parts.Row.IsStacked);
        Same(new Rect(0, 0, 136.75, SegmentHeight), parts.Active.Frame);
        Same(new Rect(140.75, 0, 136.75, SegmentHeight), parts.Completed.Frame);
        Same(new Rect((136.75 - CompletedSemiBoldAt15) / 2, (SegmentHeight - GroupHeight) / 2, CompletedSemiBoldAt15, GroupHeight), parts.CompletedGroup.Frame);
        CellsInside(parts, bounds);
        Assert.Equal(0, steady);
        Assert.Equal(1, once);
        Assert.Equal(1, passes.Invalidations);
    }

    [Fact]
    public void MeasuredAgainAtTheArrangedWidthTheRowStacksAndArrangesBothRows()
    {
        var parts = Build(activeGroup: 96, completedGroup: CompletedSemiBoldAt15);
        var passes = new LayoutPasses(parts.Row);

        passes.Measure(RowAt320);
        passes.Arrange(HalfNarrower);
        var size = passes.Measure(HalfNarrower);
        var bounds = passes.Arrange(HalfNarrower);

        Assert.True(parts.Row.IsStacked);
        Assert.Equal(SegmentHeight + 4 + SegmentHeight, size.Height, 3);
        Same(new Rect(0, SegmentHeight + 4, HalfNarrower, SegmentHeight), parts.Completed.Frame);
        CellsInside(parts, bounds);
        Assert.Equal(1, passes.Invalidations);
    }

    [Fact]
    public void MeasuredAt411AndArrangedAt320WithoutANewMeasureTheSegmentsStayInsideTheBoundsAndOneInvalidationFollows()
    {
        var parts = Build(activeGroup: 124.19, completedGroup: 125);
        var passes = new LayoutPasses(parts.Row);

        passes.Measure(RowAt411);
        var bounds = passes.Arrange(RowAt320);
        passes.Arrange(RowAt320);

        Assert.True(SegmentRowLayout.StacksRow(RowAt320, [124.19, 125]));
        Assert.False(parts.Row.IsStacked);
        CellsInside(parts, bounds);
        Assert.True(parts.Active.Frame.Right <= parts.Completed.Frame.Left);
        Assert.Equal(1, passes.Invalidations);
    }

    [Fact]
    public void ArrangedWiderThanItsStackedMeasureTheRowStaysStackedInsideItsBounds()
    {
        var parts = Build(activeGroup: 96, completedGroup: CompletedSemiBoldAt15);
        var passes = new LayoutPasses(parts.Row);

        passes.Measure(HalfNarrower);
        var bounds = passes.Arrange(RowAt320);

        Assert.True(parts.Row.IsStacked);
        Same(new Rect(0, 0, RowAt320, SegmentHeight), parts.Active.Frame);
        Same(new Rect(0, SegmentHeight + 4, RowAt320, SegmentHeight), parts.Completed.Frame);
        CellsInside(parts, bounds);
        Assert.Equal(1, passes.Invalidations);
    }

    [Fact]
    public void AParentThatMeasuresOnePixelWiderThanItArrangesCannotMakeTheSegmentsStackAndUnstack()
    {
        var parts = Build(activeGroup: 96, completedGroup: 121);
        var passes = new LayoutPasses(parts.Row);

        var modes = passes.RunParent(278.2222, 277.9259, rounds: 10, () => parts.Row.IsStacked);

        Assert.False(SegmentRowLayout.StacksRow(278.2222, [96, 121]));
        Assert.True(SegmentRowLayout.StacksRow(277.9259, [96, 121]));
        Assert.All(modes, Assert.False);
        Assert.Equal(1, passes.Invalidations);
        Assert.Equal(2, passes.Measures);
    }

    [Fact]
    public void WithTheTwinInTheSelectedWeightTheRowStacksTheSameWayWhicheverSegmentIsSelected()
    {
        var rowWidth = (2 * 135) + 4;
        var activeSelected = BuildWithTwins(firstSelected: true);
        var completedSelected = BuildWithTwins(firstSelected: false);

        new LayoutPasses(activeSelected.Row).Measure(rowWidth);
        new LayoutPasses(completedSelected.Row).Measure(rowWidth);

        Assert.False(SegmentRowLayout.Stacks(135, CompletedMediumAt15));
        Assert.True(SegmentRowLayout.Stacks(135, CompletedSemiBoldAt15));
        Assert.Equal(CompletedSemiBoldAt15, activeSelected.CompletedGroup.DesiredSize.Width, 3);
        Assert.Equal(CompletedSemiBoldAt15, completedSelected.CompletedGroup.DesiredSize.Width, 3);
        Assert.Equal("GeistMedium", activeSelected.CompletedLabel.FontFamily);
        Assert.Equal("GeistSemiBold", completedSelected.CompletedLabel.FontFamily);
        Assert.True(activeSelected.Row.IsStacked);
        Assert.Equal(activeSelected.Row.IsStacked, completedSelected.Row.IsStacked);
    }

    [Fact]
    public void EachSegmentLabelSitsInAOneCellGridAfterAnInvisibleTwinInTheSelectedWeight()
    {
        var control = Load("Shared", "Controls", "SegmentedControl.xaml");

        foreach (var prefix in new[] { "First", "Second" })
        {
            var label = control.Descendants(_maui + "Label").Single(element => element.Attribute(_xaml + "Name")?.Value == prefix + "Label");
            var cell = label.Parent!;
            var children = cell.Elements().ToList();
            var twin = children[0];

            Assert.Equal(_maui + "Grid", cell.Name);
            Assert.Empty(cell.Attributes());
            Assert.Equal(_maui + "HorizontalStackLayout", cell.Parent!.Name);
            Assert.Same(cell, cell.Parent.Elements().First());
            Assert.Equal([prefix + "LabelWidth", prefix + "Label"], children.Select(child => child.Attribute(_xaml + "Name")?.Value));
            Assert.Equal(_maui + "Label", twin.Name);
            Assert.Equal("{Binding " + prefix + "Text}", twin.Attribute("Text")?.Value);
            Assert.Equal(label.Attribute("Text")?.Value, twin.Attribute("Text")?.Value);
            Assert.Equal("GeistSemiBold", twin.Attribute("FontFamily")?.Value);
            Assert.Equal("0", twin.Attribute("Opacity")?.Value);
            Assert.Equal("True", twin.Attribute("InputTransparent")?.Value);
            Assert.Equal("False", twin.Attribute("AutomationProperties.IsInAccessibleTree")?.Value);
            Assert.All(children, child =>
            {
                Assert.Equal("Center", child.Attribute("HorizontalOptions")?.Value);
                Assert.Equal("Center", child.Attribute("VerticalOptions")?.Value);
            });
            Assert.Null(label.Attribute("FontFamily"));
            Assert.Null(label.Attribute("Opacity"));
            Assert.Equal("True", cell.Parent.Attribute("AutomationProperties.ExcludedWithChildren")?.Value);
        }
    }

    [Fact]
    public void TheGroupIsTheOverlayOfEachSegmentNotTheSegmentItself()
    {
        var parts = Build(activeGroup: 118, completedGroup: 98, surfaceWidth: 300);
        var cross = (ICrossPlatformLayout)parts.Row;

        var size = cross.CrossPlatformMeasure(RowAt320, double.PositiveInfinity);
        cross.CrossPlatformArrange(new Rect(0, 0, RowAt320, size.Height));

        Assert.Same(parts.CompletedGroup, SegmentRowLayout.GroupOf(parts.Completed));
        Assert.Same(parts.ActiveGroup, SegmentRowLayout.GroupOf(parts.Active));
        Assert.False(parts.Row.IsStacked);
        Same(new Rect(141, 0, 137, SegmentHeight), parts.Completed.Frame);
    }

    [Fact]
    public void AHiddenSegmentLeavesTheOtherAloneAcrossTheRowAndWithNoWidthLimitTheRowIsTwoUp()
    {
        var parts = Build(activeGroup: 124.19, completedGroup: 146.286);
        var unbounded = ((ICrossPlatformLayout)parts.Row).CrossPlatformMeasure(double.PositiveInfinity, double.PositiveInfinity);
        parts.Completed.IsVisible = false;
        var cross = (ICrossPlatformLayout)parts.Row;

        var size = cross.CrossPlatformMeasure(RowAt320, double.PositiveInfinity);
        cross.CrossPlatformArrange(new Rect(0, 0, RowAt320, size.Height));

        Assert.Equal((2 * 146.286) + 4, unbounded.Width, 3);
        Assert.Equal(SegmentHeight, unbounded.Height, 3);
        Assert.False(parts.Row.IsStacked);
        Same(new Rect(0, 0, RowAt320, SegmentHeight), parts.Active.Frame);
    }

    [Fact]
    public void TheTrackHoldsASegmentRowOfActiveThenCompletedEachWithItsSurfaceOverlayAndButton()
    {
        var control = Load("Shared", "Controls", "SegmentedControl.xaml");
        var track = control.Descendants(_maui + "Border").Single(element => element.Attribute(_xaml + "Name")?.Value == "Track");
        var row = Assert.Single(track.Elements());
        var segments = row.Elements().ToList();

        Assert.Equal(_controls + "SegmentRowLayout", row.Name);
        Assert.Empty(row.Attributes());
        Assert.Equal(2, segments.Count);
        Assert.DoesNotContain(control.Descendants(), element => element.Attribute("ColumnDefinitions") is not null);
        Assert.DoesNotContain(control.Descendants().Attributes(), attribute => attribute.Name.LocalName.StartsWith("Grid.", StringComparison.Ordinal));
        foreach (var (segment, prefix) in segments.Zip(new[] { "First", "Second" }))
        {
            var children = segment.Elements().ToList();
            var overlay = children[1];
            Assert.Equal(_maui + "Grid", segment.Name);
            Assert.Empty(segment.Attributes());
            Assert.Equal([_maui + "Border", _maui + "HorizontalStackLayout", _maui + "Button"], children.Select(child => child.Name));
            Assert.Equal(prefix + "Surface", children[0].Attribute(_xaml + "Name")?.Value);
            Assert.Equal("True", children[0].Attribute("InputTransparent")?.Value);
            Assert.Equal("False", children[0].Attribute("AutomationProperties.IsInAccessibleTree")?.Value);
            Assert.Equal("{StaticResource FsSpace8}", overlay.Attribute("Spacing")?.Value);
            Assert.Equal("Center", overlay.Attribute("HorizontalOptions")?.Value);
            Assert.Equal("Center", overlay.Attribute("VerticalOptions")?.Value);
            Assert.Equal("True", overlay.Attribute("InputTransparent")?.Value);
            Assert.Equal("True", overlay.Attribute("AutomationProperties.ExcludedWithChildren")?.Value);
            Assert.Equal([_maui + "Grid", _controls + "CountCapsule"], overlay.Elements().Select(element => element.Name));
            Assert.Equal([prefix + "LabelWidth", prefix + "Label"], overlay.Elements().First().Elements().Select(element => element.Attribute(_xaml + "Name")?.Value));
            Assert.Equal(prefix + "Capsule", overlay.Elements().Last().Attribute(_xaml + "Name")?.Value);
            Assert.Equal(prefix + "Button", children[2].Attribute(_xaml + "Name")?.Value);
            Assert.Equal("{StaticResource FsHeightSegment}", children[2].Attribute("MinimumHeightRequest")?.Value);
            Assert.Equal("{Binding Command}", children[2].Attribute("Command")?.Value);
            Assert.Equal("{Binding " + prefix + "Parameter}", children[2].Attribute("CommandParameter")?.Value);
            Assert.Equal("{Binding " + prefix + "Description}", children[2].Attribute("SemanticProperties.Description")?.Value);
        }
    }

    [Fact]
    public void TheControlKeepsNoLayoutCodeAndItsSelectionLookIsUnchanged()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Controls", "SegmentedControl.xaml.cs")).ReplaceLineEndings("\n");

        Assert.DoesNotContain("Measure(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeChanged", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Dispatch", source, StringComparison.Ordinal);
        Assert.Contains("ApplySegment(FirstSurface, FirstLabel, FirstLabelWidth, FirstCapsule, FirstButton, IsFirstSelected);", source, StringComparison.Ordinal);
        Assert.Contains("ApplySegment(SecondSurface, SecondLabel, SecondLabelWidth, SecondCapsule, SecondButton, !IsFirstSelected);", source, StringComparison.Ordinal);
        Assert.Contains("label.FontFamily = selected ? \"GeistSemiBold\" : \"GeistMedium\";", source, StringComparison.Ordinal);
        Assert.Contains("label.SetDynamicResource(Label.FontSizeProperty, FontSizeKey(IsCompact));\n        width.SetDynamicResource(Label.FontSizeProperty, FontSizeKey(IsCompact));", source, StringComparison.Ordinal);
        Assert.DoesNotContain("width.FontFamily", source, StringComparison.Ordinal);
        Assert.DoesNotContain("width.SetDynamicResource(Label.TextColorProperty", source, StringComparison.Ordinal);
        Assert.Equal(CapsuleVariant.Selected, SegmentedControl.CapsuleFor(selected: true, onDark: true));
        Assert.Equal(CapsuleVariant.OnDark, SegmentedControl.CapsuleFor(selected: false, onDark: true));
        Assert.Equal(CapsuleVariant.Default, SegmentedControl.CapsuleFor(selected: false, onDark: false));
    }

    [Fact]
    public void TheCapsuleStyleSetsAMinimumHeightOf24AndNoFixedHeight()
    {
        var setters = CapsuleSetters();

        Assert.False(setters.ContainsKey("HeightRequest"));
        Assert.Equal("{StaticResource FsHeightCapsule}", setters["MinimumHeightRequest"]);
        Assert.Equal(24, Number(Resolve(setters["MinimumHeightRequest"])));
        Assert.Equal(26, Number(Resolve(setters["MinimumWidthRequest"])));
        Assert.Equal("8,0", Resolve(setters["Padding"]));
        Assert.Equal("RoundRectangle 12", setters["StrokeShape"]);
    }

    [Theory]
    [InlineData(17, 24)]
    [InlineData(24, 24)]
    [InlineData(25.4, 25.4)]
    [InlineData(34, 34)]
    public void ACapsuleIsAtLeast24TallAndGrowsWithItsLabel(double labelHeight, double capsuleHeight)
    {
        var capsule = Capsule(new FixedView(14.857, labelHeight));

        var size = Resolved(capsule);

        Assert.Equal(capsuleHeight, size.Height, 3);
        Assert.Equal(14.857 + 16, size.Width, 3);
    }

    [Fact]
    public void ANarrowCountKeepsTheCapsulesMinimumWidthOf26()
    {
        var size = Resolved(Capsule(new FixedView(8, 17)));

        Assert.Equal(26, size.Width, 3);
        Assert.Equal(24, size.Height, 3);
    }

    private static (SegmentRowLayout Row, LaidOutGrid Active, LaidOutGrid Completed, FixedLayout ActiveGroup, FixedLayout CompletedGroup) Build(double activeGroup, double completedGroup, double surfaceWidth = 0)
    {
        var activeOverlay = new FixedLayout(activeGroup, 38.476) { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        var completedOverlay = new FixedLayout(completedGroup, 38.476) { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        var active = new LaidOutGrid { new FixedView(surfaceWidth, 0), activeOverlay, new FixedView(surfaceWidth, SegmentHeight) };
        var completed = new LaidOutGrid { new FixedView(surfaceWidth, 0), completedOverlay, new FixedView(surfaceWidth, SegmentHeight) };
        var row = new SegmentRowLayout();
        row.Add(active);
        row.Add(completed);
        return (row, active, completed, activeOverlay, completedOverlay);
    }

    private static (SegmentRowLayout Row, LaidOutStack CompletedGroup, WeightedLabel CompletedLabel) BuildWithTwins(bool firstSelected)
    {
        static string Weight(bool selected) => selected ? "GeistSemiBold" : "GeistMedium";

        var activeLabel = new WeightedLabel(58.5, 61.63) { FontFamily = Weight(firstSelected), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        var completedLabel = new WeightedLabel(CompletedMediumAt15, CompletedSemiBoldAt15) { FontFamily = Weight(!firstSelected), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        var activeGroup = new LaidOutStack { Spacing = 8, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        activeGroup.Add(new LaidOutGrid { Twin(58.5, 61.63), activeLabel });
        activeGroup.Add(new FixedView(26.37, 24));
        var completedGroup = new LaidOutStack { Spacing = 8, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        completedGroup.Add(new LaidOutGrid { Twin(CompletedMediumAt15, CompletedSemiBoldAt15), completedLabel });
        var row = new SegmentRowLayout();
        row.Add(new LaidOutGrid { new FixedView(0, 0), activeGroup, new FixedView(0, SegmentHeight) });
        row.Add(new LaidOutGrid { new FixedView(0, 0), completedGroup, new FixedView(0, SegmentHeight) });
        return (row, completedGroup, completedLabel);
    }

    private static WeightedLabel Twin(double medium, double semiBold) =>
        new(medium, semiBold) { FontFamily = "GeistSemiBold", Opacity = 0, InputTransparent = true, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };

    private static void CellsInside((SegmentRowLayout Row, LaidOutGrid Active, LaidOutGrid Completed, FixedLayout ActiveGroup, FixedLayout CompletedGroup) parts, Rect bounds)
    {
        LayoutPasses.Inside(bounds, parts.Active.Frame, "Active");
        LayoutPasses.Inside(bounds, parts.Completed.Frame, "Completed");
        LayoutPasses.Inside(new Rect(Point.Zero, parts.Active.Frame.Size), parts.ActiveGroup.Frame, "Active overlay");
        LayoutPasses.Inside(new Rect(Point.Zero, parts.Completed.Frame.Size), parts.CompletedGroup.Frame, "Completed overlay");
    }

    private static Border Capsule(View label)
    {
        var capsule = new Border { Content = label };
        foreach (var (property, value) in CapsuleSetters())
        {
            switch (property)
            {
                case "HeightRequest":
                    capsule.HeightRequest = Number(Resolve(value));
                    break;
                case "MinimumHeightRequest":
                    capsule.MinimumHeightRequest = Number(Resolve(value));
                    break;
                case "MinimumWidthRequest":
                    capsule.MinimumWidthRequest = Number(Resolve(value));
                    break;
                case "Padding":
                    capsule.Padding = ParseThickness(Resolve(value));
                    break;
                case "StrokeThickness":
                    capsule.StrokeThickness = Number(value);
                    break;
                default:
                    break;
            }
        }

        return capsule;
    }

    private static Size Resolved(Border capsule)
    {
        var view = (IView)capsule;
        var content = ((ICrossPlatformLayout)capsule).CrossPlatformMeasure(double.PositiveInfinity, double.PositiveInfinity);
        return new Size(
            LayoutManager.ResolveConstraints(double.PositiveInfinity, view.Width, content.Width, view.MinimumWidth, view.MaximumWidth),
            LayoutManager.ResolveConstraints(double.PositiveInfinity, view.Height, content.Height, view.MinimumHeight, view.MaximumHeight));
    }

    private static Dictionary<string, string> CapsuleSetters() =>
        Load("Shared", "Theme", "Styles.xaml").Root!
            .Elements(_maui + "Style")
            .Single(style => style.Attribute(_xaml + "Key")?.Value == "FsCapsule")
            .Elements(_maui + "Setter")
            .ToDictionary(setter => setter.Attribute("Property")!.Value, setter => setter.Attribute("Value")!.Value, StringComparer.Ordinal);

    private static string Resolve(string value)
    {
        if (!value.StartsWith(StaticResourcePrefix, StringComparison.Ordinal))
        {
            return value;
        }

        var key = value[StaticResourcePrefix.Length..^1];
        return Load("Shared", "Theme", "Metrics.xaml").Root!
            .Elements()
            .Single(element => element.Attribute(_xaml + "Key")?.Value == key)
            .Value.Trim();
    }

    private static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);

    private static Thickness ParseThickness(string value)
    {
        var parts = value.Split(',').Select(Number).ToArray();
        return parts.Length switch
        {
            1 => new Thickness(parts[0]),
            2 => new Thickness(parts[0], parts[1]),
            _ => new Thickness(parts[0], parts[1], parts[2], parts[3]),
        };
    }

    private static XDocument Load(params string[] parts) =>
        XDocument.Load(Path.Combine([RepositoryPaths.MobileApp, .. parts]));

    private static void Same(Rect expected, Rect actual)
    {
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
        Assert.Equal(expected.Width, actual.Width, 3);
        Assert.Equal(expected.Height, actual.Height, 3);
    }

    private sealed class FixedView(double width, double height) : View
    {
        public List<double> Constraints { get; } = [];

        protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
        {
            Constraints.Add(widthConstraint);
            return new Size(width, height);
        }
    }

    private sealed class FixedLayout(double width, double height) : HorizontalStackLayout
    {
        public List<double> Constraints { get; } = [];

        protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
        {
            Constraints.Add(widthConstraint);
            return new Size(width, height);
        }
    }

    private sealed class WeightedLabel(double mediumWidth, double semiBoldWidth) : Label
    {
        protected override Size MeasureOverride(double widthConstraint, double heightConstraint) =>
            new(FontFamily == "GeistSemiBold" ? semiBoldWidth : mediumWidth, 27.852);
    }

    private sealed class LaidOutStack : HorizontalStackLayout
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
