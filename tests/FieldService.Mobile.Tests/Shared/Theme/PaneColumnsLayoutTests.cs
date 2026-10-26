using System.Xml.Linq;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class PaneColumnsLayoutTests
{
    private const double PaneAt1024 = 552;
    private const double PaneAt1092 = 620;

    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";

    [Theory]
    [InlineData(552, false)]
    [InlineData(619.99, false)]
    [InlineData(620, true)]
    [InlineData(728, true)]
    [InlineData(double.PositiveInfinity, false)]
    public void ThePaneUsesTwoColumnsOnlyFromA620WidePaneAsTheBoardsAutoFitGridDoes(double width, bool twoColumns)
    {
        Assert.Equal(300, PaneColumnsLayout.MinimumColumnWidth);
        Assert.Equal(20, PaneColumnsLayout.PaneGap);
        Assert.Equal(620, PaneColumnsLayout.TwoColumnsFrom);
        Assert.Equal(twoColumns, PaneColumnsLayout.UsesTwoColumns(width));
    }

    [Fact]
    public void AtA1024WindowThePaneStacksTheInfoCardAboveTheDetailsColumn20Apart()
    {
        var (layout, info, details) = Build(allowsTwoColumns: true);
        var passes = new LayoutPasses(layout);

        var size = passes.Measure(PaneAt1024);
        passes.Arrange(PaneAt1024);

        Assert.False(layout.IsTwoColumns);
        Assert.Equal(new Rect(0, 0, PaneAt1024, info.HeightAt(PaneAt1024)), info.Frame);
        Assert.Equal(new Rect(0, info.HeightAt(PaneAt1024) + 20, PaneAt1024, details.HeightAt(PaneAt1024)), details.Frame);
        Assert.Equal(info.HeightAt(PaneAt1024) + 20 + details.HeightAt(PaneAt1024), size.Height, 6);
        Assert.Equal(PaneAt1024, info.Constraints[^1]);
        Assert.Equal(PaneAt1024, details.Constraints[^1]);
    }

    [Fact]
    public void FromA620PaneTheInfoCardAndTheDetailsColumnSitSideBySide300WideAnd20Apart()
    {
        var (layout, info, details) = Build(allowsTwoColumns: true);
        var passes = new LayoutPasses(layout);

        var size = passes.Measure(PaneAt1092);
        passes.Arrange(PaneAt1092);

        Assert.True(layout.IsTwoColumns);
        Assert.Equal(new Rect(0, 0, 300, info.HeightAt(300)), info.Frame);
        Assert.Equal(new Rect(320, 0, 300, details.HeightAt(300)), details.Frame);
        Assert.Equal(Math.Max(info.HeightAt(300), details.HeightAt(300)), size.Height, 6);
    }

    [Fact]
    public void ThePhoneLayoutNeverUsesTwoColumnsAndKeepsItsGapOf16()
    {
        var (layout, info, details) = Build(allowsTwoColumns: false);
        var passes = new LayoutPasses(layout);

        passes.Measure(768);
        passes.Arrange(768);

        Assert.False(layout.IsTwoColumns);
        Assert.Equal(16, layout.Gap);
        Assert.Equal(new Rect(0, info.HeightAt(768) + 16, 768, details.HeightAt(768)), details.Frame);
    }

    [Fact]
    public void ArrangedNarrowerThanItsTwoColumnMeasureThePaneKeepsTheMeasuredModeAndAsksForOneNewMeasure()
    {
        var (layout, info, details) = Build(allowsTwoColumns: true);
        var passes = new LayoutPasses(layout);

        passes.Measure(PaneAt1092);
        var bounds = passes.Arrange(PaneAt1092 - 0.5);
        var once = passes.Invalidations;
        passes.Arrange(PaneAt1092 - 0.5);

        Assert.True(layout.IsTwoColumns);
        Assert.Equal(1, once);
        Assert.Equal(1, passes.Invalidations);
        LayoutPasses.Inside(bounds, info.Frame, "info");
        LayoutPasses.Inside(new Rect(bounds.X, bounds.Y, bounds.Width, double.PositiveInfinity), details.Frame, "details");

        passes.Measure(PaneAt1092 - 0.5);
        passes.Arrange(PaneAt1092 - 0.5);

        Assert.False(layout.IsTwoColumns);
    }

    [Fact]
    public void JobDetailsPutsItsInfoCardAndDetailsColumnInThePaneLayoutWithThePanesGapOnlyInThePane()
    {
        var view = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Features", "Jobs", "JobDetailsView.xaml"));
        var pane = view.Descendants(_controls + "PaneColumnsLayout").Single();
        var children = pane.Elements().ToList();

        Assert.Equal("{Binding IsInPane}", pane.Attribute("AllowsTwoColumns")?.Value);
        Assert.Equal("{Binding ShowsContent}", pane.Attribute("IsVisible")?.Value);
        Assert.Equal([_maui + "Border", _maui + "VerticalStackLayout"], children.Select(child => child.Name));
        Assert.Equal("{StaticResource FsInfoListCard}", children[0].Attribute("Style")?.Value);
        Assert.Equal("{Binding DetailsSpacing}", children[1].Attribute("Spacing")?.Value);
        Assert.DoesNotContain(view.Descendants(), element => element.Attribute("ColumnDefinitions")?.Value == "*,*");
        Assert.Equal(20, PaneColumnsLayout.GapFor(allowsTwoColumns: true));
        Assert.Equal(16, PaneColumnsLayout.GapFor(allowsTwoColumns: false));
    }

    [Fact]
    public void ThePaneDetailsSpaceTheirCards20ApartAndThePhoneKeeps16()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence());
        var details = harness.CreateDetails();

        Assert.Equal(16, details.DetailsSpacing);
        details.IsInPane = true;
        Assert.Equal(20, details.DetailsSpacing);
    }

    private static (PaneColumnsLayout Layout, TextView Info, TextView Details) Build(bool allowsTwoColumns)
    {
        var info = new TextView(300 * 420);
        var details = new TextView(300 * 760);
        var layout = new PaneColumnsLayout { AllowsTwoColumns = allowsTwoColumns };
        layout.Add(info);
        layout.Add(details);
        return (layout, info, details);
    }

    private sealed class TextView(double area) : View
    {
        public List<double> Constraints { get; } = [];

        public double HeightAt(double width) => Math.Ceiling(area / width);

        protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
        {
            Constraints.Add(widthConstraint);
            var width = double.IsInfinity(widthConstraint) ? 900 : widthConstraint;
            return new Size(width, HeightAt(width));
        }
    }
}
