using System.Xml.Linq;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Build;

public sealed class BrandArtTests
{
    private const string MapPin = "M12 21s-7-6.2-7-11a7 7 0 0 1 14 0c0 4.8-7 11-7 11z";
    private const double Centre = 228;

    private static readonly XNamespace _svg = "http://www.w3.org/2000/svg";

    [Theory]
    [InlineData("Resources/Splash/splash.svg")]
    [InlineData("Resources/AppIcon/appicon.svg")]
    [InlineData("Resources/AppIcon/appiconfg.svg")]
    public void TheBrandArtIsA456CanvasWithNoTextCommentsOrTemplateArt(string file)
    {
        var text = File.ReadAllText(Path.Combine([RepositoryPaths.MobileApp, .. file.Split('/')]));
        var document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);

        Assert.Equal(_svg + "svg", document.Root!.Name);
        Assert.Equal("0 0 456 456", document.Root.Attribute("viewBox")?.Value);
        Assert.Empty(document.DescendantNodes().OfType<XComment>());
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName is "text" or "image" or "script" or "style" or "use");
        Assert.DoesNotContain(".NET", text, StringComparison.Ordinal);
        Assert.DoesNotContain("512BD4", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<!--", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSplashIsTheMarkOnDarkInsideTheSplashCircle()
    {
        var splash = Load("Resources/Splash/splash.svg");
        var rects = splash.Root!.Elements(_svg + "rect").ToList();
        var gloss = splash.Descendants(_svg + "radialGradient").Single();
        var mark = splash.Root.Elements(_svg + "g").Single();

        Assert.Equal(3, rects.Count);
        AssertAttributes(rects[0], ("x", "128"), ("y", "128"), ("width", "200"), ("height", "200"), ("rx", "62.5"), ("fill", "#FFFFFF"), ("fill-opacity", "0.10"));
        AssertAttributes(rects[1], ("x", "128"), ("y", "128"), ("width", "200"), ("height", "200"), ("rx", "62.5"), ("fill", $"url(#{gloss.Attribute("id")!.Value})"));
        AssertAttributes(gloss, ("gradientUnits", "userSpaceOnUse"), ("cx", "188"), ("cy", "168"), ("r", "212.6"));
        AssertStops(gloss, ("0", "#FFFFFF", "0.20"), ("0.6", "#FFFFFF", "0"));
        AssertAttributes(rects[2], ("x", "130"), ("y", "130"), ("width", "196"), ("height", "196"), ("rx", "60.5"), ("fill", "none"), ("stroke", "#FFFFFF"), ("stroke-opacity", "0.18"), ("stroke-width", "4"));
        AssertMark(mark, "translate(174 174) scale(4.5)", "2", "2.75");
        Assert.DoesNotContain(rects, rect => rect.Attribute("width")?.Value == "456");
        Assert.True(RoundedSquareReach(128, 62.5) <= 152);
        Assert.True(RoundedSquareReach(130, 60.5) + 2 <= 152);
    }

    [Fact]
    public void TheIconIsTheGradientTileWithThePinInsideTheAdaptiveSafeCircle()
    {
        var background = Load("Resources/AppIcon/appicon.svg");
        var foreground = Load("Resources/AppIcon/appiconfg.svg");
        var linear = background.Descendants(_svg + "linearGradient").Single();
        var gloss = background.Descendants(_svg + "radialGradient").Single();
        var fills = background.Root!.Elements(_svg + "rect").Select(rect => rect.Attribute("fill")?.Value).ToList();

        AssertAttributes(linear, ("x1", "0"), ("y1", "0"), ("x2", "1"), ("y2", "1"));
        AssertStops(linear, ("0", "#0B1F3A", null), ("1", "#0F766E", null));
        AssertAttributes(gloss, ("gradientUnits", "userSpaceOnUse"), ("cx", "136.8"), ("cy", "91.2"), ("r", "484.8"));
        AssertStops(gloss, ("0", "#FFFFFF", "0.24"), ("0.6", "#FFFFFF", "0"));
        Assert.Equal([$"url(#{linear.Attribute("id")!.Value})", $"url(#{gloss.Attribute("id")!.Value})"], fills);
        Assert.All(background.Root.Elements(_svg + "rect"), rect => AssertAttributes(rect, ("width", "456"), ("height", "456")));
        Assert.Equal(["g"], foreground.Root!.Elements().Select(element => element.Name.LocalName));
        AssertMark(foreground.Root.Elements(_svg + "g").Single(), "translate(120 120) scale(9)", "2.5", "3.25");
        Assert.True(Distance(120 + (12 * 9), 120 + (3 * 9) - (1.25 * 9)) <= 139);
        Assert.True(Distance(120 + (12 * 9), 120 + (21 * 9) + (1.25 * 9)) <= 139);
    }

    [Fact]
    public void TheProjectUsesTheBrandArtOnNavy()
    {
        var project = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "FieldService.Mobile.csproj"));

        var icon = project.Descendants("MauiIcon").Single();
        var splash = project.Descendants("MauiSplashScreen").Single();

        AssertAttributes(icon, ("Include", @"Resources\AppIcon\appicon.svg"), ("ForegroundFile", @"Resources\AppIcon\appiconfg.svg"), ("Color", "#0B1F3A"));
        AssertAttributes(splash, ("Include", @"Resources\Splash\splash.svg"), ("Color", "#0B1F3A"), ("BaseSize", "128,128"));
    }

    private static void AssertMark(XElement mark, string transform, string strokeWidth, string dotRadius)
    {
        var path = mark.Elements(_svg + "path").Single();
        var dot = mark.Elements(_svg + "circle").Single();

        Assert.Equal(transform, mark.Attribute("transform")?.Value);
        Assert.Equal(["path", "circle"], mark.Elements().Select(element => element.Name.LocalName));
        AssertAttributes(path, ("d", MapPin), ("fill", "none"), ("stroke", "#FFFFFF"), ("stroke-width", strokeWidth), ("stroke-linecap", "round"), ("stroke-linejoin", "round"));
        AssertAttributes(dot, ("cx", "12"), ("cy", "10"), ("r", dotRadius), ("fill", "#FCD34D"));
    }

    private static void AssertStops(XElement gradient, params (string Offset, string Color, string? Opacity)[] expected)
    {
        var stops = gradient.Elements(_svg + "stop")
            .Select(stop => (stop.Attribute("offset")?.Value, stop.Attribute("stop-color")?.Value, stop.Attribute("stop-opacity")?.Value))
            .ToList();

        Assert.Equal(expected.Select(stop => ((string?)stop.Offset, (string?)stop.Color, stop.Opacity)), stops);
    }

    private static void AssertAttributes(XElement element, params (string Name, string Value)[] expected)
    {
        foreach (var (name, value) in expected)
        {
            Assert.Equal(value, element.Attribute(name)?.Value);
        }
    }

    private static double RoundedSquareReach(double origin, double radius) => Distance(origin + radius, origin + radius) + radius;

    private static double Distance(double x, double y) => Math.Sqrt(Math.Pow(x - Centre, 2) + Math.Pow(y - Centre, 2));

    private static XDocument Load(string relative) =>
        XDocument.Load(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));
}
