using System.Xml.Linq;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class ListItemVisualsTests
{
    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2009/xaml";
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";

    [Fact]
    public void OnWindowsTheJobListsItemsDrawNoBackgroundAndNoSelectionBarInAnyState()
    {
        var items = Source("Platforms/Windows/ListItems.cs");
        string[] keys =
        [
            "ListViewItemBackground",
            "ListViewItemBackgroundPointerOver",
            "ListViewItemBackgroundPressed",
            "ListViewItemBackgroundSelected",
            "ListViewItemBackgroundSelectedPointerOver",
            "ListViewItemBackgroundSelectedPressed",
            "ListViewItemBackgroundSelectedDisabled",
            "ListViewItemSelectionIndicatorBrush",
            "ListViewItemSelectionIndicatorPointerOverBrush",
            "ListViewItemSelectionIndicatorPressedBrush",
            "ListViewItemSelectionIndicatorDisabledBrush",
        ];

        foreach (var key in keys)
        {
            Assert.Contains($"\"{key}\",", items, StringComparison.Ordinal);
        }

        Assert.Contains("public const string SelectionIndicatorKey = \"ListViewItemSelectionIndicatorVisualEnabled\";", items, StringComparison.Ordinal);
        var attach = items[items.IndexOf("public static void AttachList", StringComparison.Ordinal)..items.IndexOf("public static void AttachCard", StringComparison.Ordinal)];
        Assert.Contains("new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent)", attach, StringComparison.Ordinal);
        Assert.Contains("foreach (var key in ItemBrushKeys)", attach, StringComparison.Ordinal);
        Assert.Contains("list.Resources[key] = clear;", attach, StringComparison.Ordinal);
        Assert.Contains("list.Resources[SelectionIndicatorKey] = false;", attach, StringComparison.Ordinal);
        Assert.True(attach.IndexOf("list.Resources[SelectionIndicatorKey] = false;", StringComparison.Ordinal) < attach.IndexOf("list.SingleSelectionFollowsFocus = false;", StringComparison.Ordinal));
    }

    [Fact]
    public void OnWindowsListCardsDropTheirShadowAndKeepTheirStrokeWhileAndroidKeepsTheShadow()
    {
        var page = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Features", "Jobs", "MyJobsPage.xaml"));
        var listCards = page.Descendants(_maui + "CollectionView.ItemTemplate").SelectMany(template => template.Descendants(_controls + "JobCard")).ToList();
        var otherCards = page.Descendants(_controls + "JobCard").Except(listCards).ToList();
        var card = Source("Shared/Controls/JobCard.xaml.cs");

        Assert.Equal(2, listCards.Count);
        Assert.All(listCards, item => Assert.Equal("{OnPlatform WinUI=False, Default=True}", item.Attribute("HasShadow")?.Value));
        Assert.All(otherCards, item => Assert.Null(item.Attribute("HasShadow")));
        Assert.Equal("FsShadowNone", JobCard.ShadowKey(hasShadow: false, urgent: false));
        Assert.Equal("FsShadowNone", JobCard.ShadowKey(hasShadow: false, urgent: true));
        Assert.Equal("FsShadowGlass", JobCard.ShadowKey(hasShadow: true, urgent: false));
        Assert.Equal("FsShadowUrgentCard", JobCard.ShadowKey(hasShadow: true, urgent: true));
        Assert.Contains("Card.SetDynamicResource(Border.ShadowProperty, ShadowKey(HasShadow, urgent));", card, StringComparison.Ordinal);
        Assert.Contains("Card.StrokeThickness = ShowsSelection ? 2 : urgent ? 1.5 : 1;", card, StringComparison.Ordinal);
        Assert.Contains("BindableProperty.Create(\n        nameof(HasShadow),\n        typeof(bool),\n        typeof(JobCard),\n        true,", card.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void TheSelectedCompactCardsHaloStaysInsideItsListItemSoItIsNeverCut()
    {
        var page = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Features", "Jobs", "MyJobsPage.xaml"));
        var compact = page.Descendants(_controls + "JobCard").Single(card => card.Attribute(_xaml + "Name")?.Value == "CompactCard");
        var itemGrid = compact.Parent!;
        var halo = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Controls", "JobCard.xaml"))
            .Descendants(_maui + "Border").Single(border => border.Attribute(_xaml + "Name")?.Value == "SelectionHalo");

        Assert.Equal("6", itemGrid.Attribute("Padding")?.Value);
        Assert.Equal("-6", halo.Attribute("Margin")?.Value);
        Assert.Equal("RoundRectangle 24", halo.Attribute("StrokeShape")?.Value);
        Assert.Equal(24, JobCard.CompactRadius + 6);
    }

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));
}
