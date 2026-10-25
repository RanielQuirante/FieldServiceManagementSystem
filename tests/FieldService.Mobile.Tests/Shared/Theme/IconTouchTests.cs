using System.Text.RegularExpressions;
using System.Xml.Linq;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui.Controls;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed partial class IconTouchTests
{
    private const string ExcludedAttribute = "AutomationProperties.ExcludedWithChildren";

    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";

    [Fact]
    public void OnAndroidAnIconIsDrawnByAPlainViewSoATouchOnItReachesTheControlUnderIt()
    {
        var setup = Source("Platforms/Android/AndroidPlatformSetup.cs");

        Assert.Contains("handlers.AddHandler<Icon, IconHandler>();", setup, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepositoryPaths.MobileApp, "Platforms", "Android", "IconHandler.cs")), "Platforms/Android/IconHandler.cs is missing");
        var handler = Source("Platforms/Android/IconHandler.cs");
        Assert.Contains("using Microsoft.Maui.Graphics.Platform;", handler, StringComparison.Ordinal);
        Assert.Contains("ViewHandler<Icon, PlatformGraphicsView>", handler, StringComparison.Ordinal);
        Assert.Contains("[nameof(IGraphicsView.Drawable)]", handler, StringComparison.Ordinal);
        Assert.Contains("[nameof(IGraphicsView.Invalidate)]", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("PlatformTouchGraphicsView", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("OnHoverEvent", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("OnTouchEvent", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void TheIconIsTheOnlyGraphicsViewSoNoOtherDrawingTakesATouchMeantForAControl()
    {
        var subclasses = RepositoryPaths.MobileSources("*.cs")
            .Where(path => GraphicsViewSubclass().IsMatch(File.ReadAllText(path)))
            .Select(RepositoryPaths.ToRelative)
            .ToList();
        var drawings = RepositoryPaths.MobileSources("*.xaml")
            .SelectMany(path => XDocument.Load(path).Descendants(_maui + "GraphicsView").Select(_ => RepositoryPaths.ToRelative(path)))
            .ToList();

        Assert.Equal(["Shared/Controls/Icon.cs"], subclasses);
        Assert.Empty(drawings);
        Assert.True(typeof(GraphicsView).IsAssignableFrom(typeof(Icon)));
    }

    [Fact]
    public void AnIconDrawnOverAButtonAddsNoStopOfItsOwn()
    {
        var findings = new List<string>();
        var overlays = 0;
        foreach (var path in RepositoryPaths.MobileSources("*.xaml"))
        {
            var document = XDocument.Load(path, LoadOptions.SetLineInfo);
            foreach (var grid in document.Descendants(_maui + "Grid"))
            {
                var children = grid.Elements().Where(element => !element.Name.LocalName.Contains('.', StringComparison.Ordinal)).ToList();
                var button = children.FindIndex(element => element.Name == _maui + "Button");
                if (button < 0)
                {
                    continue;
                }

                foreach (var above in children.Skip(button + 1).Where(element => element.DescendantsAndSelf(_controls + "Icon").Any()))
                {
                    overlays++;
                    var leavesTheTree = above.Name == _controls + "Icon"
                        || (above.Attribute(ExcludedAttribute)?.Value == "True" && above.Attribute("InputTransparent")?.Value == "True");
                    if (!leavesTheTree)
                    {
                        findings.Add($"{RepositoryPaths.ToRelative(path)}:{((System.Xml.IXmlLineInfo)above).LineNumber}");
                    }
                }
            }
        }

        Assert.True(overlays >= 10, $"only {overlays} icons over a button were found");
        Assert.Empty(findings);
    }

    [GeneratedRegex(@"class\s+\w+\s*:\s*GraphicsView\b")]
    private static partial Regex GraphicsViewSubclass();

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));
}
