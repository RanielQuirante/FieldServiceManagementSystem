using System.Xml.Linq;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class TimeWellXamlTests
{
    private const double PhoneContentAt320 = 320 - (2 * 16) - (2 * 8) - (14 + 12);
    private const double PhoneContentAt411 = 411.4 - (2 * 16) - (2 * 8) - (14 + 12);

    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2009/xaml";
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";

    [Fact]
    public void TheWellGridHasTwoAutoRowsAndTheChevronSpansThemCentred()
    {
        var card = Load("Shared/Controls/JobCard.xaml");
        var well = Named(card, "Well");
        var grid = Named(card, "WellGrid");
        var chevron = grid.Elements(_controls + "Icon").Single();

        Assert.Same(well, grid.Parent);
        Assert.Equal("Auto,Auto", grid.Attribute("RowDefinitions")?.Value);
        Assert.Equal("*,Auto,Auto", grid.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("{StaticResource FsSpace10}", grid.Attribute("ColumnSpacing")?.Value);
        Assert.Equal("{StaticResource FsSpace4}", grid.Attribute("RowSpacing")?.Value);
        Assert.Equal("Center", grid.Attribute("VerticalOptions")?.Value);
        Assert.Equal("{StaticResource FsIconChevronRight}", chevron.Attribute("PathData")?.Value);
        Assert.Equal("2", chevron.Attribute("Grid.Column")?.Value);
        Assert.Equal("2", chevron.Attribute("Grid.RowSpan")?.Value);
        Assert.Equal("Center", chevron.Attribute("VerticalOptions")?.Value);
        Assert.Null(well.Attribute("Padding"));
        Assert.Contains("ApplyWellRows(twoRows: false);", Source(), StringComparison.Ordinal);
        Assert.Contains("Well.SetDynamicResource(Border.PaddingProperty, rows.PaddingKey);", Source(), StringComparison.Ordinal);
    }

    [Fact]
    public void AsBuiltTheTimeGroupAndThePrioritySitOnOneRow()
    {
        var card = Load("Shared/Controls/JobCard.xaml");
        var time = Named(card, "TimeGroup");
        var priority = Named(card, "WellPriority");
        var label = Named(card, "TimeLabel");

        Assert.Null(time.Attribute("Grid.Row"));
        Assert.Null(time.Attribute("Grid.Column"));
        Assert.Equal("Auto,*", time.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("{StaticResource FsSpace8}", time.Attribute("ColumnSpacing")?.Value);
        Assert.Equal("{Binding WellTime}", label.Attribute("Text")?.Value);
        Assert.Equal("{StaticResource FsTextWell}", label.Attribute("Style")?.Value);
        Assert.Equal("PriorityLabel", priority.Name.LocalName);
        Assert.Equal("1", priority.Attribute("Grid.Column")?.Value);
        Assert.Null(priority.Attribute("Grid.Row"));
    }

    [Fact]
    public void TheTwoRowPaddingTokenIsTheSpecPaddingAndTheOneRowPaddingIsUnchanged()
    {
        var metrics = Load("Shared/Theme/Metrics.xaml").Root!.Elements().ToDictionary(element => element.Attribute(_xaml + "Key")?.Value ?? string.Empty, element => element.Value);

        Assert.Equal("14,10,12,10", metrics["FsPaddingWellTwoRow"]);
        Assert.Equal("14,0,12,0", metrics["FsPaddingWell"]);
        Assert.Equal("4", metrics["FsSpace4"]);
    }

    [Fact]
    public void OneRowKeepsTheBuiltLayoutAndAddsNoRowGap()
    {
        Assert.Equal(new WellRows(TimeColumnSpan: 1, PriorityRow: 0, PriorityColumn: 1, PriorityIndent: 0, RowSpacing: 0, PaddingKey: "FsPaddingWell", PriorityColumnIsAuto: true), JobCard.WellRowsFor(twoRows: false));
    }

    [Fact]
    public void TwoRowsPutThePriorityUnderTheTimeAlignedWithItsText()
    {
        var rows = JobCard.WellRowsFor(twoRows: true);

        Assert.Equal(new WellRows(TimeColumnSpan: 2, PriorityRow: 1, PriorityColumn: 0, PriorityIndent: 24, RowSpacing: 4, PaddingKey: "FsPaddingWellTwoRow", PriorityColumnIsAuto: false), rows);
        Assert.Equal(JobCard.ClockWidth + JobCard.ClockGap, rows.PriorityIndent);
    }

    [Theory]
    [InlineData(110, 96, PhoneContentAt320, false)]
    [InlineData(110.01, 96, PhoneContentAt320, true)]
    [InlineData(132, 96, PhoneContentAt320, true)]
    [InlineData(168, 71, PhoneContentAt320, true)]
    [InlineData(132, 96, PhoneContentAt411, false)]
    [InlineData(168, 71, PhoneContentAt411, false)]
    [InlineData(0, 0, 40, false)]
    [InlineData(1, 0, 40, true)]
    public void TheWellTakesTwoRowsOnlyWhenTheTimeGroupIsWiderThanTheRoomLeftBesideThePriorityAndChevron(double timeGroup, double priority, double content, bool twoRows)
    {
        Assert.Equal(twoRows, JobCard.WellNeedsTwoRows(timeGroup, priority, content));
    }

    [Fact]
    public void TheTimeGroupIsTheClockItsGapAndTheTimeAndTheFirstRowIs216At320()
    {
        Assert.Equal(16 + 8 + 108, JobCard.TimeGroupWidth(108));
        Assert.Equal(216, PhoneContentAt320 - JobCard.ChevronWidth - JobCard.WellGap);
        Assert.Equal(246, PhoneContentAt320);
    }

    [Fact]
    public void TheModeIsSetFromTheWellWidthTheTimeTextAndTheCompactLook()
    {
        var source = Source();

        Assert.Contains("Well.SizeChanged += (_, _) => UpdateWellRows();", source, StringComparison.Ordinal);
        Assert.Contains("propertyChanged: (bindable, _, _) => ((JobCard)bindable).OnTimeWindowChanged());", source, StringComparison.Ordinal);
        Assert.Contains("        WellTime = DisplayFormats.BreakAfterEnDash(TimeWindow);\n        ScheduleWellRows();\n    }", source, StringComparison.Ordinal);
        Assert.Contains("((IView)TimeLabel).InvalidateMeasure();", source, StringComparison.Ordinal);
        Assert.Contains("        ScheduleWellRows();\n    }", source, StringComparison.Ordinal);
        Assert.Contains("TimeLabel.Measure(double.PositiveInfinity, double.PositiveInfinity)", source, StringComparison.Ordinal);
        Assert.Contains("WellPriority.Measure(double.PositiveInfinity, double.PositiveInfinity).Width - WellPriority.Margin.HorizontalThickness", source, StringComparison.Ordinal);
    }

    private static string Source() =>
        File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Controls", "JobCard.xaml.cs")).ReplaceLineEndings("\n");

    private static XElement Named(XDocument document, string name) =>
        document.Descendants().Single(element => element.Attribute(_xaml + "Name")?.Value == name);

    private static XDocument Load(string relative) =>
        XDocument.Load(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));
}
