using System.Xml;
using System.Xml.Linq;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui.Controls;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class ListAccessibilityTests
{
    private const string HeadingAttribute = "SemanticProperties.HeadingLevel";
    private const string DescriptionAttribute = "SemanticProperties.Description";
    private const string InAccessibleTreeAttribute = "AutomationProperties.IsInAccessibleTree";

    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";
    private static readonly XName _ownStop = _controls + "ListSemantics.IsOwnStop";

    [Fact]
    public void TheDayGroupsOfBothJobListsAreHeadingsThatTheScreenReaderStopsOnByThemselves()
    {
        var page = Load("Features/Jobs/MyJobsPage.xaml");
        var groups = page.Descendants(_maui + "CollectionView.GroupHeaderTemplate").SelectMany(template => template.Descendants(_maui + "Label")).ToList();

        Assert.Equal(2, groups.Count);
        Assert.All(groups, label =>
        {
            Assert.Equal("{Binding Heading}", label.Attribute("Text")?.Value);
            Assert.Equal("Level2", label.Attribute(HeadingAttribute)?.Value);
            Assert.Equal("True", label.Attribute(_ownStop)?.Value);
        });
    }

    [Fact]
    public void TheListHeadersDateTitleAndSyncLineAreSeparateStopsWithTheTitleAsTheScreenHeading()
    {
        var header = Load("Shared/Controls/AppHeader.xaml");
        var list = header.Descendants(_maui + "VerticalStackLayout").Single(element => element.Attribute("IsVisible")?.Value == "{Binding IsList}");
        var texts = list.Descendants(_maui + "Label").Where(label => label.Attribute(InAccessibleTreeAttribute)?.Value != "False").ToList();

        Assert.Equal(["{Binding Eyebrow}", "{Binding Title}", "{Binding SyncText}"], texts.Select(label => label.Attribute("Text")?.Value));
        Assert.All(texts, label => Assert.Equal("True", label.Attribute(_ownStop)?.Value));
        Assert.Equal("Level1", texts[1].Attribute(HeadingAttribute)?.Value);
    }

    [Fact]
    public void TheBannersAndTheEmptyStateThatMyJobsShowsInsideItsListAreSeparateStops()
    {
        var banner = Load("Shared/Controls/Banner.xaml");
        var empty = Load("Shared/Controls/EmptyState.xaml");
        var spoken = banner.Descendants().Single(element => element.Attribute(DescriptionAttribute)?.Value == "{Binding SpokenText}");
        var labels = empty.Descendants(_maui + "Label").Where(label => label.Ancestors().All(ancestor => ancestor.Attribute("AutomationProperties.ExcludedWithChildren")?.Value != "True")).ToList();

        Assert.Equal("True", spoken.Attribute(_ownStop)?.Value);
        Assert.Equal(["{Binding Title}", "{Binding Message}"], labels.Select(label => label.Attribute("Text")?.Value));
        Assert.All(labels, label => Assert.Equal("True", label.Attribute(_ownStop)?.Value));
        Assert.Equal("Level2", labels[0].Attribute(HeadingAttribute)?.Value);
    }

    [Fact]
    public void EveryHeadingInsideAListIsItsOwnScreenReaderStop()
    {
        var findings = new List<string>();
        foreach (var path in RepositoryPaths.MobileSources("*.xaml").Where(path => !RepositoryPaths.ToRelative(path).StartsWith("Shared/Theme/", StringComparison.Ordinal)))
        {
            var document = XDocument.Load(path, LoadOptions.SetLineInfo);
            foreach (var label in document.Descendants().Where(element => element.Attribute(HeadingAttribute) is not null))
            {
                var inList = label.Ancestors().Any(ancestor => ancestor.Name.LocalName is "CollectionView" or "CollectionView.Header" or "CollectionView.GroupHeaderTemplate" or "CollectionView.ItemTemplate")
                    || label.Ancestors().Any(ancestor => ancestor.Attribute("IsVisible")?.Value == "{Binding IsList}");
                if (inList && label.Attribute(_ownStop)?.Value != "True")
                {
                    findings.Add($"{RepositoryPaths.ToRelative(path)}:{((IXmlLineInfo)label).LineNumber}");
                }
            }
        }

        Assert.Empty(findings);
    }

    [Fact]
    public void AndroidMakesAnOwnStopAScreenReaderFocusableNode()
    {
        var setup = Source("Platforms/Android/AndroidPlatformSetup.cs");

        Assert.Contains("ViewHandler.ViewMapper.AppendToMapping(ListSemantics.IsOwnStopProperty.PropertyName", setup, StringComparison.Ordinal);
        Assert.Contains(".ScreenReaderFocusable = ListSemantics.GetIsOwnStop(", setup, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOwnStopIsOffUnlessAnElementAsksForIt()
    {
        var label = new Label();

        Assert.False(ListSemantics.GetIsOwnStop(label));
        Assert.False(label.IsSet(ListSemantics.IsOwnStopProperty));
        ListSemantics.SetIsOwnStop(label, true);
        Assert.True(ListSemantics.GetIsOwnStop(label));
        Assert.Equal("IsOwnStop", ListSemantics.IsOwnStopProperty.PropertyName);
        Assert.Throws<ArgumentNullException>(() => ListSemantics.GetIsOwnStop(null!));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1500, true)]
    [InlineData(10000, true)]
    [InlineData(10001, false)]
    [InlineData(-1, false)]
    public void AChosenSegmentKeepsScreenReaderFocusThroughTheListRebuildsOfTheNextTenSeconds(double milliseconds, bool keeps)
    {
        Assert.Equal(TimeSpan.FromSeconds(10), SegmentedControl.KeepFocusFor);
        Assert.Equal(keeps, SegmentedControl.KeepsFocus(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void AChosenSegmentTakesScreenReaderFocusBackWhenItsListRebuildsTheHeaderAroundIt()
    {
        var control = Source("Shared/Controls/SegmentedControl.xaml.cs");

        Assert.Contains("FirstButton.Clicked += OnSegmentClicked;", control, StringComparison.Ordinal);
        Assert.Contains("SecondButton.Clicked += OnSegmentClicked;", control, StringComparison.Ordinal);
        Assert.Contains("FirstButton.Loaded += OnSegmentLoaded;", control, StringComparison.Ordinal);
        Assert.Contains("SecondButton.Loaded += OnSegmentLoaded;", control, StringComparison.Ordinal);
        Assert.Contains("FocusMoves.MoveSoon(chosen, () => ReferenceEquals(chosen, _chosen) && ReferenceEquals(chosen, SelectedButton));", control, StringComparison.Ordinal);
    }

    private static XDocument Load(string relative) =>
        XDocument.Load(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));
}
