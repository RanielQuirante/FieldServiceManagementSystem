using System.Xml.Linq;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class WrapRowLayoutTests
{
    private const double PillHeight = 32;

    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";

    [Fact]
    public void OneRowOfPillsReportsThePillsHeightAndPlacesThem8Apart()
    {
        var (row, pills) = Build();
        var passes = new LayoutPasses(row);

        var size = passes.Measure(360);
        passes.Arrange(360);

        Assert.Equal(PillHeight, size.Height);
        Assert.Equal(1, row.LineCount);
        Assert.Equal(new Rect(0, 0, 74, PillHeight), pills[0].Frame);
        Assert.Equal(new Rect(82, 0, 70, PillHeight), pills[1].Frame);
        Assert.Equal(new Rect(160, 0, 76, PillHeight), pills[2].Frame);
    }

    [Fact]
    public void ARowThatWrapsReportsBothRowsAndThe8BetweenThemSoNoPillIsCut()
    {
        var (row, pills) = Build();
        var passes = new LayoutPasses(row);

        var size = passes.Measure(160);
        passes.Arrange(160);

        Assert.Equal(PillHeight + 8 + PillHeight, size.Height);
        Assert.Equal(2, row.LineCount);
        Assert.Equal(new Rect(0, 40, 76, PillHeight), pills[2].Frame);
        foreach (var pill in pills)
        {
            LayoutPasses.Inside(new Rect(0, 0, 160, size.Height), pill.Frame, "pill");
        }
    }

    [Fact]
    public void EachPillIsMeasuredWithTheRowsWidthAsItsLimit()
    {
        var (row, pills) = Build();
        var passes = new LayoutPasses(row);

        var size = passes.Measure(70);

        Assert.Equal((3 * PillHeight) + (2 * 8), size.Height);
        Assert.All(pills, pill => Assert.Equal(70, pill.Constraints[^1]));
    }

    [Fact]
    public void ArrangedNarrowerThanItsOneRowMeasureTheRowKeepsItsLinesAndAsksForOneNewMeasure()
    {
        var (row, pills) = Build();
        var passes = new LayoutPasses(row);

        passes.Measure(236);
        passes.Arrange(235.5);
        passes.Arrange(235.5);

        Assert.Equal(1, row.LineCount);
        Assert.Equal(1, passes.Invalidations);
        Assert.Equal(0, pills[2].Frame.Top);

        passes.Measure(235.5);

        Assert.Equal(2, row.LineCount);
    }

    [Fact]
    public void TheLineBreaksFollowTheWidthsAndTheSpacing()
    {
        Assert.Equal([0, 0, 0], WrapRowLayout.LinesFor(236, [74, 70, 76], 8));
        Assert.Equal([0, 0, 1], WrapRowLayout.LinesFor(235.9, [74, 70, 76], 8));
        Assert.Equal([0, 1, 2], WrapRowLayout.LinesFor(70, [74, 70, 76], 8));
        Assert.Equal([0, 0, 0], WrapRowLayout.LinesFor(double.PositiveInfinity, [74, 70, 76], 8));
        Assert.Equal(0, WrapRowLayout.HeightOf([]));
    }

    [Fact]
    public void TheInspectionHeroPutsItsThreeCountPillsInTheWrapRowWithNoMarginsAndNoFlexLayout()
    {
        var page = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Features", "Inspection", "InspectionPage.xaml"));
        var row = page.Descendants(_controls + "WrapRowLayout").Single();
        var pills = row.Elements(_maui + "Border").ToList();

        Assert.Equal("{StaticResource FsSpace8}", row.Attribute("ColumnSpacing")?.Value);
        Assert.Equal("{StaticResource FsSpace8}", row.Attribute("RowSpacing")?.Value);
        Assert.Equal("True", row.Attribute("AutomationProperties.ExcludedWithChildren")?.Value);
        Assert.Equal(3, pills.Count);
        Assert.All(pills, pill => Assert.Equal("{StaticResource FsCountPillOnDark}", pill.Attribute("Style")?.Value));
        Assert.All(pills, pill => Assert.Null(pill.Attribute("Margin")));
        Assert.Empty(page.Descendants(_maui + "FlexLayout"));
        Assert.Equal(8, WrapRowLayout.DefaultSpacing);
    }

    private static (WrapRowLayout Row, FixedView[] Pills) Build()
    {
        FixedView[] pills = [new(74, PillHeight), new(70, PillHeight), new(76, PillHeight)];
        var row = new WrapRowLayout();
        foreach (var pill in pills)
        {
            row.Add(pill);
        }

        return (row, pills);
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
}
