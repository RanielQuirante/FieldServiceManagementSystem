using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed partial class SystemBarsAndTypeTests
{
    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2009/xaml";
    private static readonly string[] _themeFiles = ["Colors", "Brushes", "Metrics", "Typography", "Icons", "Styles"];
    private static readonly double[] _allowedLineHeights = [1.05, 1.1, 1.15];

    [Theory]
    [InlineData("Features/SignIn/SignInPage.xaml", "SignInHeader")]
    [InlineData("Features/Jobs/MyJobsPage.xaml", "ListHeader")]
    [InlineData("Features/Jobs/JobDetailsView.xaml", "DetailsHeader")]
    public void TheHeroLiesUnderTheStatusBarBecauseNoContainerAddsTheTopInset(string file, string hero)
    {
        var document = Load(file);
        var header = Named(document, hero);

        var containers = header.Ancestors().Where(element => element.Name.Namespace == _maui).ToList();

        Assert.Equal("AppHeader", header.Name.LocalName);
        Assert.Equal("None", TopEdge(document.Root!));
        Assert.Equal("None", document.Root!.Elements(_maui + "Grid").Single().Attribute("SafeAreaEdges")?.Value);
        Assert.All(containers.Where(element => element.Attribute("SafeAreaEdges") is not null), element => Assert.Equal("None", TopEdge(element)));
        Assert.All(containers.Where(element => element.Name.LocalName == "RefreshView"), element => Assert.Equal("None", TopEdge(element)));
    }

    [Fact]
    public void MyJobsAndItsSheetCoverTheWholeWindow()
    {
        var page = Load("Features/Jobs/MyJobsPage.xaml");
        var sheet = Load("Shared/Controls/BottomSheet.xaml");
        var template = sheet.Descendants(_maui + "ControlTemplate").Single().Elements(_maui + "Grid").Single();
        var sheetBorder = template.Elements(_maui + "Border").Single();

        Assert.Equal("None", page.Root!.Attribute("SafeAreaEdges")?.Value);
        Assert.Equal("None", page.Root.Elements(_maui + "Grid").Single().Attribute("SafeAreaEdges")?.Value);
        Assert.Equal("None", sheet.Root!.Attribute("SafeAreaEdges")?.Value);
        Assert.Equal("None,None,None,SoftInput", template.Attribute("SafeAreaEdges")?.Value);
        Assert.Equal("{TemplateBinding SheetPadding}", sheetBorder.Attribute("Padding")?.Value);
        Assert.Contains(page.Descendants(), element => element.Name.LocalName == "CollectionView.Footer" && element.Descendants().Any(child => child.Name.LocalName == "SystemBarSpacer"));
    }

    [Theory]
    [InlineData(0, 28)]
    [InlineData(24, 52)]
    [InlineData(-5, 28)]
    public void TheSheetContentIsPaddedAboveTheNavigationBar(double navigationBar, double bottom)
    {
        Assert.Equal(new Thickness(0, 10, 0, bottom), BottomSheet.PaddingFor(navigationBar));
        Assert.Equal(new Thickness(16, 0, 16, 0), BottomSheet.ContentPaddingFor(hasHeader: false));
    }

    [Theory]
    [InlineData(0, 54, false)]
    [InlineData(1, 54, true)]
    [InlineData(0.38, 54, true)]
    [InlineData(40, 54, true)]
    [InlineData(900, 54, true)]
    [InlineData(-6, 54, false)]
    [InlineData(900, 0, false)]
    public void TheProtectionLayerCoversTheStatusBarAsSoonAsTheContentLeavesItsTop(double offset, double inset, bool needed)
    {
        Assert.Equal(needed, StatusBarProtection.IsNeeded(offset, inset));
    }

    [Fact]
    public void ThereIsNoLayerAtOffsetZeroAndALayerAtOffsetOne()
    {
        var protection = new StatusBarProtection();
        protection.ApplySystemBarInsets(54.1, 24);

        Assert.False(protection.IsVisible);

        protection.ApplyScrollOffset(1);
        Assert.True(protection.IsShown);
        Assert.True(protection.IsVisible);

        protection.ApplyScrollOffset(0);
        Assert.False(protection.IsShown);
        Assert.False(protection.IsVisible);
    }

    [Theory]
    [InlineData("Features/SignIn/SignInPage.xaml", "SignInScroll")]
    [InlineData("Features/Jobs/MyJobsPage.xaml", "JobsList")]
    [InlineData("Features/Jobs/JobDetailsView.xaml", "DetailsScroll")]
    public void EveryScreenWithAHeroWatchesItsScrollForProtection(string file, string scroll)
    {
        var document = Load(file);

        var protection = Assert.Single(document.Descendants(), element => element.Name.LocalName == "StatusBarProtection");

        Assert.Equal($"{{x:Reference {scroll}}}", protection.Attribute("ScrollSource")?.Value);
        Assert.Null(protection.Attribute("Hero"));
        Assert.True(Named(document, scroll).IsBefore(protection));
        Assert.True(protection.ElementsAfterSelf().All(element => element.Name.LocalName is "BottomSheet" or "ActionDock" or "SystemBarSpacer"));
    }

    [Fact]
    public void ThePanesDetailsNeverCoverTheStatusBar()
    {
        var protection = Load("Features/Jobs/JobDetailsView.xaml").Descendants().Single(element => element.Name.LocalName == "StatusBarProtection");

        Assert.Equal("{Binding ShowsPushedHeader}", protection.Attribute("IsActive")?.Value);

        var inactive = new StatusBarProtection { IsActive = false };
        var active = new StatusBarProtection();
        foreach (var layer in new[] { inactive, active })
        {
            layer.ApplySystemBarInsets(54, 24);
            layer.ApplyScrollOffset(120);
        }

        Assert.False(inactive.IsVisible);
        Assert.True(active.IsVisible);

        inactive.IsActive = true;
        Assert.True(inactive.IsVisible);
    }

    [Fact]
    public void TheProtectionLayerIsOpaqueNavyAndHoldsTheStatusBarHeight()
    {
        var protection = new StatusBarProtection();

        protection.ApplySystemBarInsets(54.1, 24);
        protection.ApplyScrollOffset(40);

        var colors = Load("Shared/Theme/Colors.xaml").Root!.Elements();
        Assert.Equal("FsNavy950Brush", StatusBarProtection.BrushKey);
        Assert.Equal("#071426", colors.Single(element => element.Attribute(_xaml + "Key")?.Value == "FsNavy950").Value);
        Assert.Equal("{StaticResource FsNavy950}", colors.Single(element => element.Attribute(_xaml + "Key")?.Value == "FsNavy950Brush").Attribute("Color")?.Value);
        Assert.Equal(54.1, protection.HeightRequest, 3);
        Assert.True(protection.InputTransparent);
        Assert.False(AutomationProperties.GetIsInAccessibleTree(protection));
        Assert.True(protection.IsVisible);
    }

    [Fact]
    public void TheStatusBarIconsStayLightOnEveryScreen()
    {
        var setup = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Platforms", "Android", "AndroidPlatformSetup.cs"));

        var assignments = StatusBarAppearance().Matches(setup).Select(match => match.Groups[1].Value).ToList();

        Assert.Equal(["false"], assignments);
    }

    [Fact]
    public void TheDockReachesTheBottomEdgeWithItsButtonsAboveTheNavigationBar()
    {
        var page = Load("Features/Jobs/JobDetailsPage.xaml");
        var view = Load("Features/Jobs/JobDetailsView.xaml");
        var dock = Load("Shared/Controls/ActionDock.xaml");
        var surface = dock.Descendants(_maui + "ControlTemplate").Single().Elements(_maui + "Border").Single();
        var bottomRow = view.Root!.Elements(_maui + "Grid").Single().Elements().Where(element => element.Attribute("Grid.Row")?.Value == "1").Select(element => element.Name.LocalName).ToList();

        Assert.Equal("None", page.Root!.Attribute("SafeAreaEdges")?.Value);
        Assert.Equal("None", dock.Root!.Attribute("SafeAreaEdges")?.Value);
        Assert.Equal("None", surface.Attribute("SafeAreaEdges")?.Value);
        Assert.Equal("{TemplateBinding DockPadding}", surface.Attribute("Padding")?.Value);
        Assert.Equal("-1,0,-1,-1", surface.Attribute("Margin")?.Value);
        Assert.Equal(1d, ActionDock.Bleed);
        Assert.Equal(["SystemBarSpacer", "ActionDock"], bottomRow);
        Assert.Equal(new Thickness(16, 16, 16, 28), VisibleInset(ActionDock.PaddingFor(0)));
        Assert.Equal(new Thickness(16, 16, 16, 52), VisibleInset(ActionDock.PaddingFor(24)));
        Assert.Equal(new Thickness(16, 16, 16, 28), VisibleInset(ActionDock.PaddingFor(-3)));
    }

    private static Thickness VisibleInset(Thickness padding) =>
        new(padding.Left - ActionDock.Bleed, padding.Top, padding.Right - ActionDock.Bleed, padding.Bottom - ActionDock.Bleed);

    [Fact]
    public void EveryThemeKeyHasOneMeaning()
    {
        var keys = _themeFiles
            .SelectMany(file => Load($"Shared/Theme/{file}.xaml").Root!.Elements()
                .Select(element => element.Attribute(_xaml + "Key")?.Value)
                .OfType<string>()
                .Select(key => (Key: key, File: file)))
            .ToList();

        var duplicates = keys.GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} ({string.Join(", ", group.Select(entry => entry.File))})")
            .ToList();

        Assert.Empty(duplicates);
        Assert.Contains(keys, entry => entry.Key == "FsWell" && entry.File == "Styles");
        Assert.Contains(keys, entry => entry.Key == "FsWellBrush");
    }

    [Fact]
    public void TheSkeletonCardPaintsItsTimeWellWithTheWellBrush()
    {
        var card = Load("Shared/Controls/JobCard.xaml");

        var well = Assert.Single(card.Descendants(_maui + "BoxView"), element => element.Attribute("HeightRequest")?.Value == "{StaticResource FsHeightWell}");

        Assert.Equal("{StaticResource FsWellBrush}", well.Attribute("Background")?.Value);
        Assert.Null(well.Attribute("Color"));
    }

    [Fact]
    public void EveryLineHeightIsTheSpecValueDividedByGeistsNaturalHeight()
    {
        var findings = new List<string>();
        foreach (var path in RepositoryPaths.MobileSources("*.xaml"))
        {
            var document = XDocument.Load(path, LoadOptions.SetLineInfo);
            var values = document.Descendants().Attributes("LineHeight").Select(attribute => attribute.Value)
                .Concat(document.Descendants(_maui + "Setter").Where(setter => setter.Attribute("Property")?.Value == "LineHeight").Select(setter => setter.Attribute("Value")!.Value));
            findings.AddRange(values
                .Where(value => !_allowedLineHeights.Contains(double.Parse(value, CultureInfo.InvariantCulture)))
                .Select(value => $"{RepositoryPaths.ToRelative(path)} LineHeight {value}"));
        }

        Assert.Empty(findings);
        Assert.Equal("1.15", LineHeightOf("FsTextBody"));
        Assert.Equal("1.1", LineHeightOf("FsTextMeta"));
        Assert.Equal("1.05", LineHeightOf("FsTextPrompt"));
        Assert.Null(LineHeightOf("FsTextSignInTitle"));
        Assert.Null(LineHeightOf("FsTextSection"));
    }

    [Fact]
    public void TheBannerSpansCarryTheBannerLineHeight()
    {
        var banner = Load("Shared/Controls/Banner.xaml");
        var message = banner.Descendants(_maui + "Label").First(label => label.Attribute("Style")?.Value == "{StaticResource FsTextBanner}");

        var spans = message.Descendants(_maui + "Span").ToList();

        Assert.Equal(3, spans.Count);
        Assert.All(spans, span => Assert.Equal("1.1", span.Attribute("LineHeight")?.Value));
    }

    private static string? LineHeightOf(string style) =>
        Load("Shared/Theme/Typography.xaml").Root!.Elements(_maui + "Style")
            .Single(element => element.Attribute(_xaml + "Key")?.Value == style)
            .Elements(_maui + "Setter")
            .SingleOrDefault(setter => setter.Attribute("Property")?.Value == "LineHeight")?
            .Attribute("Value")?.Value;

    private static string TopEdge(XElement element)
    {
        var value = element.Attribute("SafeAreaEdges")?.Value ?? string.Empty;
        var edges = value.Split(',', StringSplitOptions.TrimEntries);
        return edges.Length switch
        {
            1 => edges[0],
            2 => edges[1],
            4 => edges[1],
            _ => value,
        };
    }

    private static XElement Named(XDocument document, string name) =>
        document.Descendants().Single(element => element.Attribute(_xaml + "Name")?.Value == name);

    private static XDocument Load(string relative) =>
        XDocument.Load(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));

    [GeneratedRegex(@"AppearanceLightStatusBars\s*=\s*(\w+)", RegexOptions.CultureInvariant)]
    private static partial Regex StatusBarAppearance();
}
