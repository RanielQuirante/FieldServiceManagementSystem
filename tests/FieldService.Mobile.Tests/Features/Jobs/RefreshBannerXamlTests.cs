using System.Xml.Linq;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Features.Jobs;

public sealed class RefreshBannerXamlTests
{
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";

    [Theory]
    [InlineData("Features/Jobs/MyJobsPage.xaml", 2)]
    [InlineData("Features/Jobs/JobDetailsView.xaml", 1)]
    public void TheRefreshBannerSitsWhereTheOfflineBannerGoesAndOffersTryAgain(string file, int expected)
    {
        var document = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, file));

        var banners = document.Descendants(_controls + "Banner")
            .Where(banner => banner.Attribute("IsVisible")?.Value == "{Binding HasRefreshError}")
            .ToList();

        Assert.Equal(expected, banners.Count);
        Assert.All(banners, banner =>
        {
            Assert.Equal("Error", banner.Attribute("Variant")?.Value);
            Assert.Equal("{Binding RefreshError.Title}", banner.Attribute("Title")?.Value);
            Assert.Equal("{Binding RefreshError.Message}", banner.Attribute("Message")?.Value);
            Assert.Equal("{Binding RefreshError.Reference}", banner.Attribute("Reference")?.Value);
            Assert.Equal("Try again", banner.Attribute("ActionText")?.Value);
            Assert.Equal("{Binding RetryCommand}", banner.Attribute("ActionCommand")?.Value);
            var before = banner.ElementsBeforeSelf().Last();
            Assert.Equal("Offline", before.Attribute("Variant")?.Value);
        });
    }
}
