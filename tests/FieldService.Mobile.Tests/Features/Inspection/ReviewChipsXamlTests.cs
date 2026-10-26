using System.Xml.Linq;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Features.Inspection;

public sealed class ReviewChipsXamlTests
{
    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";

    [Fact]
    public void ReviewAndSubmitsResultChipsWrapLikeTheChecklistPills()
    {
        var page = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Features", "Inspection", "ReviewAndSubmitPage.xaml"));

        Assert.Empty(page.Descendants(_maui + "FlexLayout"));
        var row = Assert.Single(page.Descendants(_controls + "WrapRowLayout"));
        Assert.Equal("{StaticResource FsSpace8}", row.Attribute("ColumnSpacing")?.Value);
        Assert.Equal("{StaticResource FsSpace8}", row.Attribute("RowSpacing")?.Value);
        var chips = row.Elements(_controls + "StatusChip").ToList();
        Assert.Equal(["Pass", "Fail", "NotApplicable"], chips.Select(chip => chip.Attribute("Result")?.Value));
        Assert.Equal(["{Binding PassCount}", "{Binding FailCount}", "{Binding NotApplicableCount}"], chips.Select(chip => chip.Attribute("Count")?.Value));
        Assert.All(chips, chip => Assert.Null(chip.Attribute("Margin")));
    }
}
