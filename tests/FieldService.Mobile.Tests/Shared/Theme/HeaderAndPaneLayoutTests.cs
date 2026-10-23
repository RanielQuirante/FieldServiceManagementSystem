using System.Globalization;
using System.Xml.Linq;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class HeaderAndPaneLayoutTests
{
    private const double MinimumTarget = 48;
    private const string StaticResourcePrefix = "{StaticResource ";

    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2009/xaml";
    private static readonly string[] _tappableParts = ["Segment", "Button", "Primary", "Secondary", "Quiet", "Row", "Input", "Track"];

    [Fact]
    public void TappableHeightTokensAreAtLeast48AndTheCompactSegmentHeightIsGone()
    {
        var metrics = ThemeValues("Metrics.xaml");

        var tappable = metrics
            .Where(pair => pair.Key.StartsWith("FsHeight", StringComparison.Ordinal) && _tappableParts.Any(part => pair.Key.Contains(part, StringComparison.Ordinal)))
            .ToList();

        Assert.DoesNotContain("FsHeightSegmentCompact", metrics.Keys);
        Assert.Contains(tappable, pair => pair.Key == "FsHeightSegment");
        Assert.Contains(tappable, pair => pair.Key == "FsHeightDesktopButton");
        Assert.All(tappable, pair => Assert.True(Number(pair.Value) >= MinimumTarget, pair.Key));
    }

    [Fact]
    public void TheWindowsHeroActionsUseTheWebButtonStyleAt48()
    {
        var style = Style("Styles.xaml", "FsSecondaryOnDarkDesktopButton");
        var label = Style("Typography.xaml", "FsTextButtonLabelDesktop");
        var view = Load("Features", "Jobs", "JobDetailsView.xaml");
        var heroActions = view.Descendants().Single(element => element.Attribute("IsVisible")?.Value == "{Binding ShowsHeroActions}");

        Assert.Equal("{StaticResource FsSecondaryOnDarkButton}", style.Attribute("BasedOn")?.Value);
        Assert.Equal(MinimumTarget, Number(Resolve(Setter(style, "MinimumHeightRequest"))));
        Assert.Equal("12", Setter(style, "CornerRadius"));
        Assert.Equal(14, Number(Resolve(Setter(style, "FontSize"))));
        Assert.Equal("16,0", Resolve(Setter(style, "Padding")));
        Assert.Equal(14, Number(Resolve(Setter(label, "FontSize"))));
        var buttons = heroActions.Descendants(_maui + "Button").ToList();
        var primary = Style("Styles.xaml", "FsPrimaryOnDarkDesktopButton");
        Assert.Equal(
            ["{Binding StartJobCommand}", "{Binding OpenChecklistCommand}", "{Binding OpenStatusSheetCommand}", "{Binding OpenStatusSheetCommand}", "{Binding NavigateCommand}", "{Binding CallCommand}"],
            buttons.Select(button => button.Attribute("Command")?.Value));
        Assert.Equal(
            ["{Binding ShowsStartJob}", "{Binding ShowsInspectionAction}", "{Binding ShowsUpdateStatusSecondary}", "{Binding ShowsUpdateStatusPrimary}"],
            buttons.Take(4).Select(button => button.Parent!.Attribute("IsVisible")?.Value));
        Assert.Equal(
            ["FsPrimaryOnDarkDesktopButton", "FsPrimaryOnDarkDesktopButton", "FsSecondaryOnDarkDesktopButton", "FsPrimaryOnDarkDesktopButton", "FsSecondaryOnDarkDesktopButton", "FsSecondaryOnDarkDesktopButton"],
            buttons.Select(button => button.Attribute("Style")?.Value?.Replace("{StaticResource ", string.Empty, StringComparison.Ordinal).TrimEnd('}')));
        Assert.Equal("{StaticResource FsSecondaryOnDarkDesktopButton}", primary.Attribute("BasedOn")?.Value);
        Assert.Equal("{StaticResource FsPrimaryOnDarkBrush}", Setter(primary, "Background"));
        Assert.Equal("{StaticResource FsInk}", Setter(primary, "TextColor"));
        Assert.All(heroActions.Descendants().Where(element => element.Name.LocalName == "Icon"), icon => Assert.Equal("18", icon.Attribute("Size")?.Value));
        Assert.All(heroActions.Descendants(_maui + "Label"), text => Assert.Equal("{StaticResource FsTextButtonLabelDesktop}", text.Attribute("Style")?.Value));
        Assert.All(heroActions.Descendants(_maui + "HorizontalStackLayout"), row => Assert.Equal("{StaticResource FsSpace8}", row.Attribute("Spacing")?.Value));
    }

    [Fact]
    public void TheWindowsTopBarIs72WithA48AvatarButtonAnd15PointInitials()
    {
        var header = Load("Shared", "Controls", "AppHeader.xaml");
        var topBar = header.Descendants(_maui + "Grid").Single(grid => grid.Attribute("IsVisible")?.Value == "{Binding IsDesktopTopBar}");
        var avatar = topBar.Elements(_maui + "Button").Single();
        var avatarStyle = Style("Styles.xaml", "FsIconButtonOnDark");
        var padding = Resolve(topBar.Attribute("Padding")!.Value).Split(',').Select(Number).ToList();

        Assert.Equal("{StaticResource FsIconButtonOnDark}", avatar.Attribute("Style")?.Value);
        Assert.Equal("{Binding AvatarText}", avatar.Attribute("Text")?.Value);
        Assert.Equal(MinimumTarget, Number(Resolve(Setter(avatarStyle, "WidthRequest"))));
        Assert.Equal(MinimumTarget, Number(Resolve(Setter(avatarStyle, "HeightRequest"))));
        Assert.Equal(15, Number(Resolve(Setter(avatarStyle, "FontSize"))));
        Assert.Equal([24, 12], padding);
        Assert.Equal(72, padding[1] + MinimumTarget + padding[1]);
        Assert.Equal(64, Number(Resolve(topBar.Attribute("MinimumHeightRequest")!.Value)));
    }

    [Fact]
    public void TheWindowsSegmentsKeep48WithThe15PointLabel()
    {
        var page = Load("Features", "Jobs", "MyJobsPage.xaml");
        var control = Load("Shared", "Controls", "SegmentedControl.xaml");
        var typography = ThemeValues("Typography.xaml");
        var segments = page.Descendants().Where(element => element.Name.LocalName == "SegmentedControl").ToList();

        Assert.Equal(2, segments.Count);
        Assert.Null(segments.Single(segment => segment.Attribute("IsOnDark")?.Value == "True").Attribute("IsCompact"));
        Assert.Equal("True", segments.Single(segment => segment.Attribute("IsOnDark") is null).Attribute("IsCompact")?.Value);
        Assert.Equal(15, Number(typography[SegmentedControl.FontSizeKey(compact: true)]));
        Assert.Equal(16, Number(typography[SegmentedControl.FontSizeKey(compact: false)]));
        var buttons = control.Descendants(_maui + "Button").ToList();
        Assert.Equal(2, buttons.Count);
        Assert.All(buttons, button => Assert.Equal(MinimumTarget, Number(Resolve(button.Attribute("MinimumHeightRequest")!.Value))));
    }

    [Fact]
    public void AnEmptySyncLineLeavesTheAccessibilityTreeWhileTheRefreshButtonHoldsTheRow()
    {
        var header = Load("Shared", "Controls", "AppHeader.xaml");
        var label = header.Descendants(_maui + "Label").Single(element => element.Attribute("Text")?.Value == "{Binding SyncText}");
        var icon = header.Descendants().Single(element => element.Attribute(_xaml + "Name")?.Value == "SyncIcon");
        var row = label.Parent!.Parent!;
        var refresh = row.Descendants(_maui + "Button").Single();

        Assert.Equal("{Binding HasSyncText}", label.Attribute("IsVisible")?.Value);
        Assert.Equal("{Binding HasSyncText}", icon.Attribute("IsVisible")?.Value);
        Assert.Equal("{Binding RefreshCommand}", refresh.Attribute("Command")?.Value);
        Assert.Null(refresh.Attribute("IsVisible"));
        Assert.Equal(MinimumTarget, Number(Resolve(Setter(Style("Styles.xaml", "FsIconButtonOnDark"), "HeightRequest"))));
    }

    private static XDocument Load(params string[] parts) =>
        XDocument.Load(Path.Combine([RepositoryPaths.MobileApp, .. parts]));

    private static XElement Style(string file, string key) =>
        Load("Shared", "Theme", file).Root!
            .Elements(_maui + "Style")
            .Single(style => style.Attribute(_xaml + "Key")?.Value == key);

    private static string Setter(XElement style, string property) =>
        style.Elements(_maui + "Setter").Single(setter => setter.Attribute("Property")?.Value == property).Attribute("Value")!.Value;

    private static Dictionary<string, string> ThemeValues(string file) =>
        Load("Shared", "Theme", file).Root!
            .Elements()
            .Where(element => element.Attribute(_xaml + "Key") is not null && element.Name.LocalName != "Style")
            .ToDictionary(element => element.Attribute(_xaml + "Key")!.Value, element => element.Value.Trim(), StringComparer.Ordinal);

    private static string Resolve(string value)
    {
        if (!value.StartsWith(StaticResourcePrefix, StringComparison.Ordinal))
        {
            return value;
        }

        var key = value[StaticResourcePrefix.Length..^1];
        var metrics = ThemeValues("Metrics.xaml");
        return metrics.TryGetValue(key, out var metric) ? metric : ThemeValues("Typography.xaml")[key];
    }

    private static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);
}
