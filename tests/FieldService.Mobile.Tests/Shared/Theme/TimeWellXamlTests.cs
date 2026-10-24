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
    public void TheWellHoldsATimeWellLayoutOfTheTimeGroupThePriorityAndTheChevronCentred()
    {
        var card = Load("Shared/Controls/JobCard.xaml");
        var well = Named(card, "Well");
        var layout = Named(card, "WellContent");
        var children = layout.Elements().ToList();
        var chevron = children[TimeWellLayout.ChevronIndex];

        Assert.Equal(_controls + "TimeWellLayout", layout.Name);
        Assert.Same(well, layout.Parent);
        Assert.Equal("Center", layout.Attribute("VerticalOptions")?.Value);
        Assert.Equal(3, children.Count);
        Assert.Equal("TimeGroup", children[TimeWellLayout.TimeIndex].Attribute(_xaml + "Name")?.Value);
        Assert.Equal("WellPriority", children[TimeWellLayout.PriorityIndex].Attribute(_xaml + "Name")?.Value);
        Assert.Equal(_controls + "Icon", chevron.Name);
        Assert.Equal("{StaticResource FsIconChevronRight}", chevron.Attribute("PathData")?.Value);
        Assert.Equal("20", chevron.Attribute("Size")?.Value);
        Assert.Equal("Center", chevron.Attribute("VerticalOptions")?.Value);
        Assert.All(children, child => Assert.DoesNotContain(child.Attributes(), attribute => attribute.Name.LocalName.StartsWith("Grid.", StringComparison.Ordinal)));
        Assert.Null(well.Attribute("Padding"));
        Assert.Null(layout.Attribute("Padding"));
        Assert.DoesNotContain(card.Descendants(), element => element.Attribute(_xaml + "Name")?.Value == "WellGrid");
    }

    [Fact]
    public void AsBuiltTheTimeGroupIsTheClockCellAndTheTimeLabel()
    {
        var card = Load("Shared/Controls/JobCard.xaml");
        var time = Named(card, "TimeGroup");
        var priority = Named(card, "WellPriority");
        var label = Named(card, "TimeLabel");

        Assert.Equal("Auto,*", time.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("{StaticResource FsSpace8}", time.Attribute("ColumnSpacing")?.Value);
        Assert.Equal("Center", time.Attribute("VerticalOptions")?.Value);
        Assert.Same(time, label.Parent);
        Assert.Equal("{Binding WellTime}", label.Attribute("Text")?.Value);
        Assert.Equal("{StaticResource FsTextWell}", label.Attribute("Style")?.Value);
        Assert.Equal("PriorityLabel", priority.Name.LocalName);
        Assert.Null(priority.Attribute("Margin"));
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
    public void TheCardKeepsNoLayoutCodeAndNothingMeasuresAWellChildOutsideTheLayoutPass()
    {
        var source = Source();

        Assert.Contains("public bool IsTwoRowWell => WellContent.IsTwoRows;", source, StringComparison.Ordinal);
        Assert.Contains("private void OnTimeWindowChanged() => WellTime = DisplayFormats.BreakAfterEnDash(TimeWindow);", source, StringComparison.Ordinal);
        Assert.Contains("propertyChanged: (bindable, _, _) => ((JobCard)bindable).OnTimeWindowChanged());", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Measure(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeChanged", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Dispatch", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid.Set", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyWellRows", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateWellRows", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ScheduleWellRows", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Well.SetDynamicResource(Border.PaddingProperty", source, StringComparison.Ordinal);
    }

    private static string Source() =>
        File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Controls", "JobCard.xaml.cs")).ReplaceLineEndings("\n");

    private static XElement Named(XDocument document, string name) =>
        document.Descendants().Single(element => element.Attribute(_xaml + "Name")?.Value == name);

    private static XDocument Load(string relative) =>
        XDocument.Load(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));
}
