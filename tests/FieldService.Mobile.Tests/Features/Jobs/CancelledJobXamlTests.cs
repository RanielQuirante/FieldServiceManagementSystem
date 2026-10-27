using System.Xml.Linq;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Features.Jobs;

public sealed class CancelledJobXamlTests
{
    private const string Excluded = "AutomationProperties.ExcludedWithChildren";
    private const string Description = "SemanticProperties.Description";

    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2009/xaml";
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";

    [Fact]
    public void TheHeroStatusLineAndReasonAreOneNodeNamedByTheStatusDescriptionWhenTheHeaderHasOne()
    {
        var header = Load("Shared/Controls/AppHeader.xaml");
        var block = header.Descendants(_maui + "VerticalStackLayout").Single(element => element.Attribute("IsVisible")?.Value == "{Binding HasStatusLine}");
        var parts = block.Elements().ToList();
        var line = parts[0];
        var reason = parts[1];
        var source = Source("Shared/Controls/AppHeader.xaml.cs");

        Assert.Equal("{Binding StatusDescription}", block.Attribute(Description)?.Value);
        Assert.Equal("{StaticResource FsSpace4}", block.Attribute("Spacing")?.Value);
        Assert.Equal(["Grid", "Label"], parts.Select(part => part.Name.LocalName));
        Assert.All(parts, part => Assert.Equal("{Binding HasStatusDescription}", part.Attribute(Excluded)?.Value));
        Assert.Contains(line.Descendants(_maui + "Label"), label => label.Attribute("Text")?.Value == "{Binding StatusLineText}");
        Assert.Equal("StatusLineIcon", line.Descendants(_controls + "Icon").Single().Attribute(_xaml + "Name")?.Value);
        Assert.Equal("{Binding ReasonText}", reason.Attribute("Text")?.Value);
        Assert.Equal("{Binding HasReasonText}", reason.Attribute("IsVisible")?.Value);
        Assert.Equal("{StaticResource FsMarginHeroReason}", reason.Attribute("Margin")?.Value);
        Assert.Equal("3", reason.Attribute("MaxLines")?.Value);
        Assert.Equal("TailTruncation", reason.Attribute("LineBreakMode")?.Value);
        Assert.Contains("HasStatusDescription = !string.IsNullOrWhiteSpace(StatusDescription);", source, StringComparison.Ordinal);
        Assert.Contains("StatusLineIcon.SetDynamicResource(Icon.PathDataProperty, StatusChip.ResourceKeys(SummaryStatus).Icon);", source, StringComparison.Ordinal);
        Assert.Equal("FsIconXCircle", StatusChip.ResourceKeys(JobStatus.Cancelled).Icon);
    }

    [Fact]
    public void JobDetailsHandsTheHeaderTheReasonLineAndTheStatusDescription()
    {
        var header = Load("Features/Jobs/JobDetailsView.xaml").Descendants(_controls + "AppHeader").Single();

        Assert.Equal("{Binding StatusLine}", header.Attribute("StatusLineText")?.Value);
        Assert.Equal("{Binding ReasonLine}", header.Attribute("ReasonText")?.Value);
        Assert.Equal("{Binding StatusDescription}", header.Attribute("StatusDescription")?.Value);
        Assert.Equal("{Binding Status}", header.Attribute("SummaryStatus")?.Value);
    }

    [Fact]
    public void ThePaneHeroKeepsItsMetaAndTheReasonInOneColumnReadAsOneDescription()
    {
        var view = Load("Features/Jobs/JobDetailsView.xaml");
        var hero = view.Descendants(_maui + "Border").Single(element => element.Attribute("IsVisible")?.Value == "{Binding ShowsPaneHero}");
        var block = hero.Descendants(_maui + "VerticalStackLayout").Single(element => element.Attribute("IsVisible")?.Value == "{Binding ShowsPaneMeta}");
        var labels = block.Elements().ToList();

        Assert.Equal("{Binding PaneStatusDescription}", block.Attribute(Description)?.Value);
        Assert.Equal("{StaticResource FsSpace4}", block.Attribute("Spacing")?.Value);
        Assert.Equal(["{Binding PaneMeta}", "{Binding ReasonLine}"], labels.Select(label => label.Attribute("Text")?.Value));
        Assert.All(labels, label =>
        {
            Assert.Equal("Label", label.Name.LocalName);
            Assert.Equal("{StaticResource FsTextMetaOnDark}", label.Attribute("Style")?.Value);
            Assert.Equal("{Binding HasStatusDescription}", label.Attribute(Excluded)?.Value);
        });
        Assert.Equal("{Binding HasReasonLine}", labels[1].Attribute("IsVisible")?.Value);
        Assert.Equal("3", labels[1].Attribute("MaxLines")?.Value);
        Assert.Equal("TailTruncation", labels[1].Attribute("LineBreakMode")?.Value);
        Assert.Null(labels[1].Attribute("Margin"));
        Assert.DoesNotContain(view.Descendants(), element => element.Attributes().Any(attribute => attribute.Value.Contains("HoldReasonLine", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheDockTheWindowsActionRowAndTheChecklistFormFollowOnlyTheirViewModelFlags()
    {
        var view = Load("Features/Jobs/JobDetailsView.xaml");
        var dock = view.Descendants(_controls + "ActionDock").Single();
        var actions = view.Descendants(_controls + "WrapRowLayout").Single(element => element.Attribute("IsVisible")?.Value == "{Binding ShowsHeroActions}");
        var row = view.Descendants(_controls + "InfoRow").Single(element => element.Attribute("Label")?.Value == "Checklist");
        var link = view.Descendants(_controls + "LinkCard").Single();

        Assert.Equal("{Binding ShowsDock}", dock.Attribute("IsVisible")?.Value);
        Assert.NotNull(actions);
        Assert.Equal("{Binding ShowsChecklistRow}", row.Attribute("IsVisible")?.Value);
        Assert.Equal("{Binding ChecklistName}", row.Attribute("Value")?.Value);
        Assert.Equal("{Binding ShowsChecklistLink}", link.Attribute("IsVisible")?.Value);
        Assert.Equal("{Binding OpenChecklistCommand}", link.Attribute("Command")?.Value);
    }

    [Fact]
    public void TheCancelledChipUsesTheBrushesColourAndIconTheThemeAlreadyHolds()
    {
        var keys = StatusChip.ResourceKeys(JobStatus.Cancelled);
        var theme = new[] { "Brushes.xaml", "Colors.xaml", "Icons.xaml" }
            .SelectMany(file => Load("Shared/Theme/" + file).Root!.Elements())
            .Select(element => element.Attribute(_xaml + "Key")?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(("FsChipCancelledBrush", "FsStatusCancelledRingBrush", "FsStatusCancelled", "FsIconXCircle"), keys);
        Assert.Contains(keys.Fill, theme);
        Assert.Contains(keys.Ring, theme);
        Assert.Contains(keys.Text, theme);
        Assert.Contains(keys.Icon, theme);
    }

    private static XDocument Load(string relative) =>
        XDocument.Load(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')])).ReplaceLineEndings("\n");
}
