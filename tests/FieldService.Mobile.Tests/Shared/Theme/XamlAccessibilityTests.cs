using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class XamlAccessibilityTests
{
    private const double MinimumTarget = 48;
    private const string DescriptionAttribute = "SemanticProperties.Description";
    private const string HeadingAttribute = "SemanticProperties.HeadingLevel";
    private const string InAccessibleTreeAttribute = "AutomationProperties.IsInAccessibleTree";

    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2009/xaml";

    private static readonly HashSet<string> _interactiveTypes = new(StringComparer.Ordinal)
    {
        "Button",
        "ImageButton",
        "Entry",
        "Editor",
        "CheckBox",
        "RadioButton",
        "Picker",
        "DatePicker",
        "TimePicker",
        "SearchBar",
    };

    private static readonly HashSet<string> _decorativeTypes = new(StringComparer.Ordinal)
    {
        "Border",
        "BoxView",
        "Path",
        "Ellipse",
        "Rectangle",
        "Line",
    };

    private static readonly Lazy<IReadOnlyDictionary<string, double>> _metrics = new(LoadMetrics);

    [Fact]
    public void IconOnlyButtonsAndImageButtonsHaveADescription()
    {
        var findings = new List<string>();
        foreach (var (path, document) in AppXaml())
        {
            foreach (var element in document.Descendants())
            {
                var name = element.Name.LocalName;
                var iconOnly = name == "ImageButton" || (name == "Button" && element.Attribute("Text") is null);
                if (element.Name.Namespace == _maui && iconOnly && element.Attribute(DescriptionAttribute) is null)
                {
                    findings.Add(Where(path, element));
                }
            }
        }

        Assert.Empty(findings);
    }

    [Fact]
    public void DecorativeLayersAreOutOfTheAccessibilityTree()
    {
        var findings = new List<string>();
        foreach (var (path, document) in AppXaml())
        {
            foreach (var element in document.Descendants().Where(element => element.Name.Namespace == _maui && _decorativeTypes.Contains(element.Name.LocalName)))
            {
                var decorative = element.Attribute("InputTransparent")?.Value == "True" || !element.Elements().Any(child => !child.Name.LocalName.Contains('.', StringComparison.Ordinal));
                if (decorative && element.Attribute(InAccessibleTreeAttribute)?.Value != "False")
                {
                    findings.Add(Where(path, element));
                }
            }
        }

        Assert.Empty(findings);
    }

    [Fact]
    public void AJobCardIsOneNodeWithTheRoleAndActionOfAButton()
    {
        var card = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Controls", "JobCard.xaml"));
        var setup = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Platforms", "Android", "AndroidPlatformSetup.cs"));
        var handler = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Platforms", "Android", "TappableCardHandler.cs"));
        var layers = card.Root!.Elements(_maui + "Grid").Single().Elements(_maui + "Border").ToList();

        Assert.True(typeof(ITappableCard).IsAssignableFrom(typeof(JobCard)));
        Assert.Equal("android.widget.Button", TappableCard.AndroidClassName);
        Assert.True(TappableCard.ActsAsButton(isLoading: false, "JOB-1047, Walk-in freezer temperature alarm, Assigned"));
        Assert.False(TappableCard.ActsAsButton(isLoading: true, "JOB-1047, Walk-in freezer temperature alarm, Assigned"));
        Assert.False(TappableCard.ActsAsButton(isLoading: false, " "));
        Assert.Equal(3, layers.Count);
        Assert.All(layers.Skip(1), layer => Assert.Equal("True", layer.Attribute("AutomationProperties.ExcludedWithChildren")?.Value));
        Assert.DoesNotContain(card.Descendants(), element => _interactiveTypes.Contains(element.Name.LocalName));
        Assert.Contains("AddHandler<JobCard, TappableCardHandler>()", setup, StringComparison.Ordinal);
        Assert.Contains("info.ClassName = TappableCard.AndroidClassName;", handler, StringComparison.Ordinal);
        Assert.Contains("info.Clickable = true;", handler, StringComparison.Ordinal);
        Assert.Contains("AccessibilityNodeInfo.AccessibilityAction.ActionClick", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void ALinkCardIsOneButtonNodeNamedByItsDescription()
    {
        var card = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Controls", "LinkCard.xaml"));
        var grid = card.Root!.Elements(_maui + "Grid").Single();

        var button = Assert.Single(card.Descendants(), element => _interactiveTypes.Contains(element.Name.LocalName));
        var surface = grid.Elements(_maui + "Border").Single();

        Assert.Equal("Button", button.Name.LocalName);
        Assert.Equal("{Binding LinkDescription}", button.Attribute(DescriptionAttribute)?.Value);
        Assert.Equal("{Binding Command}", button.Attribute("Command")?.Value);
        Assert.Equal("True", surface.Attribute("AutomationProperties.ExcludedWithChildren")?.Value);
        Assert.Equal("False", surface.Attribute(InAccessibleTreeAttribute)?.Value);
    }

    [Fact]
    public void TheIconControlIsOutOfTheAccessibilityTree()
    {
        var icon = new Icon { PathData = "M5 12h14", Size = 20 };

        Assert.False(AutomationProperties.GetIsInAccessibleTree(icon));
        Assert.True(AutomationProperties.GetExcludedWithChildren(icon));
        Assert.True(icon.InputTransparent);
        Assert.Equal(20, icon.WidthRequest);
        Assert.Equal(20, icon.HeightRequest);
    }

    [Theory]
    [InlineData(14, 1.3125)]
    [InlineData(16, 1.5)]
    [InlineData(20, 5.0 / 3.0)]
    [InlineData(24, 2)]
    [InlineData(28, 7.0 / 3.0)]
    public void IconStrokesScaleWithTheSizeAsTheSpecDrawsThem(double size, double thickness)
    {
        Assert.Equal(thickness, Icon.StrokeThicknessFor(size), 6);
    }

    [Fact]
    public void EveryIconPathParsesInsideTheTwentyFourUnitBox()
    {
        var icons = IconPaths();
        var findings = new List<string>();

        foreach (var (key, data) in icons)
        {
            var icon = new Icon { PathData = data };
            var bounds = icon.Geometry?.GetBoundsByFlattening() ?? RectF.Zero;
            if (icon.Geometry is null || icon.Geometry.Count == 0 || bounds.Left < -0.01 || bounds.Top < -0.01 || bounds.Right > 24.01 || bounds.Bottom > 24.01 || bounds.Width <= 0)
            {
                findings.Add($"{key} {bounds}");
            }
        }

        Assert.Equal(44, icons.Count);
        Assert.Empty(findings);
    }

    [Fact]
    public void InteractiveStylesHaveAMinimumHeightOf48()
    {
        var styles = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Theme", "Styles.xaml"));
        var byKey = styles.Root!.Elements(_maui + "Style")
            .Where(style => style.Attribute(_xaml + "Key") is not null)
            .ToDictionary(style => style.Attribute(_xaml + "Key")!.Value, StringComparer.Ordinal);
        var findings = new List<string>();

        foreach (var style in styles.Root.Elements(_maui + "Style").Where(style => _interactiveTypes.Contains(style.Attribute("TargetType")?.Value ?? string.Empty)))
        {
            var height = StyleHeight(style, byKey);
            if (height is null || height < MinimumTarget)
            {
                findings.Add($"{style.Attribute(_xaml + "Key")?.Value ?? style.Attribute("TargetType")!.Value}: {height?.ToString(CultureInfo.InvariantCulture) ?? "none"}");
            }
        }

        Assert.Empty(findings);
    }

    [Fact]
    public void InteractiveControlsInXamlKeepAMinimumHeightOf48()
    {
        var findings = new List<string>();
        foreach (var (path, document) in AppXaml())
        {
            foreach (var element in document.Descendants().Where(element => element.Name.Namespace == _maui && _interactiveTypes.Contains(element.Name.LocalName)))
            {
                if (IsVisualOnly(element))
                {
                    if (!element.Ancestors().Any(ancestor => HeightOf(ancestor) >= MinimumTarget))
                    {
                        findings.Add(Where(path, element));
                    }

                    continue;
                }

                var styled = element.Attribute("Style") is not null || element.Name.LocalName is "Entry" or "Editor" or "CheckBox" or "RadioButton";
                var height = HeightOf(element);
                if ((height is not null && height < MinimumTarget) || (!styled && height is null))
                {
                    findings.Add(Where(path, element));
                }
            }
        }

        Assert.Empty(findings);
    }

    [Fact]
    public void EveryPageHasALevelOneHeading()
    {
        var appHeader = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Controls", "AppHeader.xaml"));
        Assert.Contains(appHeader.Descendants(), element => element.Attribute(HeadingAttribute)?.Value == "Level1");

        var findings = new List<string>();
        var all = AppXaml().ToList();
        var views = all
            .Where(entry => entry.Document.Root!.Name == _maui + "ContentView")
            .ToDictionary(entry => Path.GetFileNameWithoutExtension(entry.Path), entry => entry.Document, StringComparer.Ordinal);
        var pages = all.Where(entry => entry.Document.Root!.Name == _maui + "ContentPage").ToList();
        foreach (var (path, document) in pages)
        {
            var hasHeading = HasTopHeading(document)
                || document.Descendants().Any(element => element.Name.Namespace != _maui
                    && views.TryGetValue(element.Name.LocalName, out var view)
                    && element.Name.LocalName != "AppHeader"
                    && HasTopHeading(view));
            if (!hasHeading)
            {
                findings.Add(RepositoryPaths.ToRelative(path));
            }
        }

        Assert.NotEmpty(pages);
        Assert.Empty(findings);
    }

    private static bool HasTopHeading(XDocument document) =>
        document.Descendants().Any(element => element.Attribute(HeadingAttribute)?.Value == "Level1")
        || document.Descendants().Any(element => element.Name.LocalName == "AppHeader");

    private static IEnumerable<(string Path, XDocument Document)> AppXaml() =>
        RepositoryPaths.MobileSources("*.xaml")
            .Where(path => !RepositoryPaths.ToRelative(path).StartsWith("Shared/Theme/", StringComparison.Ordinal))
            .Select(path => (path, XDocument.Load(path, LoadOptions.SetLineInfo)));

    private static bool IsVisualOnly(XElement element) =>
        element.Attribute("InputTransparent")?.Value == "True" && element.Attribute(InAccessibleTreeAttribute)?.Value == "False";

    private static double? HeightOf(XElement element) =>
        Resolve(element.Attribute("MinimumHeightRequest")?.Value) ?? Resolve(element.Attribute("HeightRequest")?.Value);

    private static string Where(string path, XElement element) =>
        $"{RepositoryPaths.ToRelative(path)}:{((IXmlLineInfo)element).LineNumber} {element.Name.LocalName}";

    private static IReadOnlyList<(string Key, string Data)> IconPaths()
    {
        var icons = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Theme", "Icons.xaml"));
        return [.. icons.Root!.Elements(_xaml + "String").Select(element => (element.Attribute(_xaml + "Key")!.Value, element.Value))];
    }

    private static double? StyleHeight(XElement style, IReadOnlyDictionary<string, XElement> byKey)
    {
        foreach (var property in new[] { "MinimumHeightRequest", "HeightRequest" })
        {
            var setter = style.Elements(_maui + "Setter").FirstOrDefault(element => element.Attribute("Property")?.Value == property);
            if (Resolve(setter?.Attribute("Value")?.Value) is { } value)
            {
                return value;
            }
        }

        var basedOn = style.Attribute("BasedOn")?.Value;
        var key = basedOn is null ? null : ResourceKey(basedOn);
        return key is not null && byKey.TryGetValue(key, out var parent) ? StyleHeight(parent, byKey) : null;
    }

    private static double? Resolve(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (ResourceKey(value) is { } key)
        {
            return _metrics.Value.TryGetValue(key, out var metric) ? metric : null;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    private static string? ResourceKey(string value)
    {
        const string Prefix = "{StaticResource ";
        return value.StartsWith(Prefix, StringComparison.Ordinal) && value.EndsWith('}') ? value[Prefix.Length..^1].Trim() : null;
    }

    private static Dictionary<string, double> LoadMetrics()
    {
        var metrics = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Theme", "Metrics.xaml"));
        return metrics.Root!.Elements(_xaml + "Double")
            .ToDictionary(
                element => element.Attribute(_xaml + "Key")!.Value,
                element => double.Parse(element.Value, CultureInfo.InvariantCulture),
                StringComparer.Ordinal);
    }
}
