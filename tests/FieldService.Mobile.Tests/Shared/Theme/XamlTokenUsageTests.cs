using System.Text.RegularExpressions;
using System.Xml.Linq;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed partial class XamlTokenUsageTests
{
    private const string ThemeFolder = "Shared/Theme/";
    private const string PlatformsFolder = "Platforms/";

    private static readonly string[] _colourAttributeNames =
    [
        "Background",
        "Stroke",
        "Fill",
        "Color",
    ];

    [Fact]
    public void TheScanCoversTheAppXamlAndCSharpSources()
    {
        var xaml = RepositoryPaths.MobileSources("*.xaml").Select(RepositoryPaths.ToRelative).ToList();
        var csharp = RepositoryPaths.MobileSources("*.cs").Select(RepositoryPaths.ToRelative).ToList();

        Assert.Contains("Features/SignIn/SignInPage.xaml", xaml);
        Assert.Contains("Shared/Controls/FormField.xaml", xaml);
        Assert.Contains("Shared/Theme/Colors.xaml", xaml);
        Assert.Contains("Features/SignIn/SignInViewModel.cs", csharp);
        Assert.Contains("Shared/Controls/Icon.cs", csharp);
    }

    [Fact]
    public void NoXamlOutsideTheThemeHoldsAHexOrFunctionalColour()
    {
        var findings = new List<string>();
        foreach (var path in XamlOutsideTheme())
        {
            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                if (HexColour().IsMatch(lines[index]) || FunctionalColour().IsMatch(lines[index]))
                {
                    findings.Add($"{RepositoryPaths.ToRelative(path)}:{index + 1}");
                }
            }
        }

        Assert.Empty(findings);
    }

    [Fact]
    public void ColourAttributesOutsideTheThemeUseResourcesOrBindings()
    {
        var findings = new List<string>();
        foreach (var path in XamlOutsideTheme())
        {
            var document = XDocument.Load(path, LoadOptions.SetLineInfo);
            foreach (var attribute in document.Descendants().Attributes().Where(attribute => IsColourProperty(attribute.Name.LocalName)))
            {
                if (!attribute.Value.TrimStart().StartsWith('{'))
                {
                    findings.Add($"{RepositoryPaths.ToRelative(path)}:{((System.Xml.IXmlLineInfo)attribute).LineNumber} {attribute.Name.LocalName}={attribute.Value}");
                }
            }

            foreach (var setter in document.Descendants().Where(element => element.Name.LocalName == "Setter"))
            {
                var property = setter.Attribute("Property")?.Value;
                var value = setter.Attribute("Value")?.Value;
                if (property is not null && value is not null && IsColourProperty(property) && !value.TrimStart().StartsWith('{'))
                {
                    findings.Add($"{RepositoryPaths.ToRelative(path)}:{((System.Xml.IXmlLineInfo)setter).LineNumber} Setter {property}={value}");
                }
            }
        }

        Assert.Empty(findings);
    }

    [Fact]
    public void NoCSharpOutsideTheThemeAndPlatformsBuildsALiteralColour()
    {
        var findings = new List<string>();
        foreach (var path in RepositoryPaths.MobileSources("*.cs"))
        {
            var relative = RepositoryPaths.ToRelative(path);
            if (relative.StartsWith(ThemeFolder, StringComparison.Ordinal) || relative.StartsWith(PlatformsFolder, StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                if (CodeColour().IsMatch(lines[index]))
                {
                    findings.Add($"{relative}:{index + 1}");
                }
            }
        }

        Assert.Empty(findings);
    }

    [Fact]
    public void TheAppIsLightOnlyAndNeverUsesAppThemeBinding()
    {
        var sources = RepositoryPaths.MobileSources("*.xaml").Concat(RepositoryPaths.MobileSources("*.cs"));
        var findings = sources
            .Where(path => File.ReadAllText(path).Contains("AppThemeBinding", StringComparison.Ordinal))
            .Select(RepositoryPaths.ToRelative)
            .ToList();

        Assert.Empty(findings);
        Assert.Contains("UserAppTheme = AppTheme.Light", File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "App.xaml.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void AppXamlMergesTheSixThemeDictionariesInDependencyOrder()
    {
        XNamespace maui = "http://schemas.microsoft.com/dotnet/2021/maui";
        var document = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "App.xaml"));

        var sources = document
            .Descendants(maui + "ResourceDictionary")
            .Select(dictionary => dictionary.Attribute("Source")?.Value)
            .OfType<string>()
            .ToList();

        Assert.Equal(
            [
                "Shared/Theme/Colors.xaml",
                "Shared/Theme/Brushes.xaml",
                "Shared/Theme/Metrics.xaml",
                "Shared/Theme/Typography.xaml",
                "Shared/Theme/Icons.xaml",
                "Shared/Theme/Styles.xaml",
            ],
            sources);
        Assert.All(sources, source => Assert.True(File.Exists(Path.Combine(RepositoryPaths.MobileApp, source))));
    }

    private static IEnumerable<string> XamlOutsideTheme() =>
        RepositoryPaths.MobileSources("*.xaml")
            .Where(path => !RepositoryPaths.ToRelative(path).StartsWith(ThemeFolder, StringComparison.Ordinal));

    private static bool IsColourProperty(string name)
    {
        var property = name[(name.LastIndexOf('.') + 1)..];
        return _colourAttributeNames.Contains(property, StringComparer.Ordinal)
            || property.EndsWith("Color", StringComparison.Ordinal)
            || property.EndsWith("Brush", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"(?<![&\w])#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{4}|[0-9A-Fa-f]{3})\b")]
    private static partial Regex HexColour();

    [GeneratedRegex(@"\b(?:rgba?|hsla?|hsva?)\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex FunctionalColour();

    [GeneratedRegex(@"\bColor\.From\w+\s*\(|\bColors\.\w")]
    private static partial Regex CodeColour();
}
