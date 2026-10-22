using System.Text.RegularExpressions;
using System.Xml.Linq;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui.Graphics;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed partial class IconPathTests
{
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2009/xaml";

    public static TheoryData<string, string> Icons()
    {
        var data = new TheoryData<string, string>();
        foreach (var (key, value) in IconData())
        {
            data.Add(key, value);
        }

        return data;
    }

    private static Dictionary<string, string> IconData()
    {
        var icons = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Theme", "Icons.xaml"));
        return icons.Root!.Elements(_xaml + "String")
            .ToDictionary(element => element.Attribute(_xaml + "Key")!.Value, element => element.Value, StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Icons))]
    public void EverySubpathIsClosedExactlyWhereItsDataEndsWithZ(string key, string data)
    {
        var expected = MoveCommand().Split(data)
            .Where(part => part.Length > 0)
            .Select(part => part.TrimEnd().EndsWith('z') || part.TrimEnd().EndsWith('Z'))
            .ToList();

        var path = IconPath.Parse(data);

        Assert.True(expected.Count == path.SubPathCount, $"{key}: {path.SubPathCount} subpaths, expected {expected.Count}");
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.True(expected[index] == path.IsSubPathClosed(index), $"{key}: subpath {index} closed {path.IsSubPathClosed(index)}");
        }

        var closes = Enumerable.Range(0, path.OperationCount).Count(index => path.GetSegmentType(index) == PathOperation.Close);
        Assert.Equal(expected.Count(closed => closed), closes);
    }

    [Theory]
    [InlineData("FsLogoMarkPin")]
    [InlineData("FsIconMapPin")]
    [InlineData("FsIconEye")]
    public void ASmoothCurveThatStartsASubpathTakesTheCurrentPointAsItsFirstControlPoint(string key)
    {
        var path = IconPath.Parse(IconData()[key]);

        Assert.Equal(PathOperation.Move, path.GetSegmentType(0));
        Assert.Equal(PathOperation.Cubic, path.GetSegmentType(1));
        Assert.Equal(path[0], path[1]);
    }

    [Fact]
    public void TheClockHandsAreAnOpenPolylineAfterTheFace()
    {
        var path = IconPath.Parse("M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0M12 7v5l3 2");

        Assert.Equal(2, path.SubPathCount);
        Assert.False(path.IsSubPathClosed(0));
        Assert.False(path.IsSubPathClosed(1));
        var last = path.OperationCount;
        Assert.Equal(
            [PathOperation.Move, PathOperation.Line, PathOperation.Line],
            [path.GetSegmentType(last - 3), path.GetSegmentType(last - 2), path.GetSegmentType(last - 1)]);
        Assert.Equal(new PointF(15, 14), path[path.Count - 1]);
    }

    [Fact]
    public void TheMailFlapStaysOpenAfterTheClosedEnvelope()
    {
        var path = IconPath.Parse("M5 5h14a2 2 0 0 1 2 2v10a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-10a2 2 0 0 1 2 -2zM3 7l9 6 9-6");

        Assert.True(path.IsSubPathClosed(0));
        Assert.False(path.IsSubPathClosed(1));
        Assert.Equal(new PointF(21, 7), path[path.Count - 1]);
    }

    [Fact]
    public void ArcsFollowTheirCircle()
    {
        var path = IconPath.Parse("M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0");
        var center = new PointF(12, 12);

        var flattened = path.GetFlattenedPath(0.01f, false);
        Assert.NotNull(flattened);
        Assert.True(flattened!.Count > 16);
        for (var index = 0; index < flattened.Count; index++)
        {
            Assert.Equal(9, flattened[index].Distance(center), 0.02);
        }

        Assert.Equal(new PointF(3, 12), path[path.Count - 1]);
    }

    [Theory]
    [InlineData("M8 6h.01", 8.01f, 6f)]
    [InlineData("M10 3.5-3.5 7", -3.5f, 7f)]
    [InlineData("M1e1 2E0l1.5.5", 11.5f, 2.5f)]
    [InlineData("M2,2 L4,4", 4f, 4f)]
    public void NumbersAreReadInEverySvgForm(string data, float x, float y)
    {
        var path = IconPath.Parse(data);

        Assert.Equal(new PointF(x, y), path[path.Count - 1]);
    }

    [Theory]
    [InlineData(2.25)]
    [InlineData(2)]
    public void TheDashedCircleUsesThreeUnitDashesAndGaps(double strokeWidth)
    {
        var pattern = Icon.DashPatternFor(strokeWidth);

        Assert.All(pattern, length => Assert.Equal(3, length * strokeWidth, 4));
    }

    [Fact]
    public void TheUrgentBadgeStrokeOverridesTheSizeRule()
    {
        var icon = new Icon { PathData = "M12 9v4", Size = 16, StrokeWidthOverride = 2.5 };

        Assert.Equal(2.5, icon.StrokeWidthOverride);
        Assert.Null(icon.DashPattern);
        icon.IsDashed = true;
        Assert.Equal(Icon.DashPatternFor(2.5), icon.DashPattern);
    }

    [GeneratedRegex("(?=[Mm])")]
    private static partial Regex MoveCommand();
}
