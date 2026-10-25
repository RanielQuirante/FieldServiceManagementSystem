using System.Text.RegularExpressions;
using System.Xml.Linq;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed partial class ShadowContainerTests
{
    private const string NoShadow = "{StaticResource FsShadowNone}";
    private const string StylesFile = "Shared/Theme/Styles.xaml";

    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2009/xaml";

    [Fact]
    public void NoStyleStateOrTriggerTurnsAShadowOffWithNothingSoMauiNeverRebuildsTheShadowContainer()
    {
        var findings = new List<string>();
        foreach (var path in RepositoryPaths.MobileSources("*.xaml"))
        {
            var document = XDocument.Load(path, LoadOptions.SetLineInfo);
            foreach (var (element, value) in ShadowValues(document))
            {
                if (IsNull(value))
                {
                    findings.Add($"{RepositoryPaths.ToRelative(path)}:{((System.Xml.IXmlLineInfo)element).LineNumber}");
                }
            }
        }

        Assert.Empty(findings);
    }

    [Fact]
    public void TheDisabledLookOfEveryShadowedButtonUsesTheInvisibleShadowToken()
    {
        var styles = Load(StylesFile);
        foreach (var key in new[] { "FsSecondaryButton", "FsIconButton" })
        {
            var style = styles.Root!.Elements(_maui + "Style").Single(element => element.Attribute(_xaml + "Key")?.Value == key);
            Assert.NotNull(StyleShadow(style));
            var disabled = style.Descendants(_maui + "VisualState").Single(state => state.Attribute(_xaml + "Name")?.Value == "Disabled");
            Assert.Equal(NoShadow, disabled.Descendants(_maui + "Setter").Single(setter => setter.Attribute("Property")?.Value == "Shadow").Attribute("Value")?.Value);
        }

        var primary = Load("Shared/Controls/PrimaryButton.xaml");
        var core = primary.Descendants(_maui + "Button").Single(button => button.Attribute(_xaml + "Name")?.Value == "CoreButton");
        Assert.Equal("{StaticResource FsShadowPrimary}", core.Attribute("Shadow")?.Value);
        var trigger = core.Descendants(_maui + "DataTrigger").Single(element => element.Attribute("Binding")?.Value == "{Binding ShowsDisabledLook}");
        Assert.Equal(NoShadow, trigger.Elements(_maui + "Setter").Single(setter => setter.Attribute("Property")?.Value == "Shadow").Attribute("Value")?.Value);

        var none = styles.Root!.Elements(_maui + "Shadow").Single(shadow => shadow.Attribute(_xaml + "Key")?.Value == "FsShadowNone");
        Assert.Equal("{StaticResource FsTransparentBrush}", none.Attribute("Brush")?.Value);
        Assert.Equal("0", none.Attribute("Opacity")?.Value);
        Assert.Equal("0", none.Attribute("Radius")?.Value);
        Assert.Equal("0,0", none.Attribute("Offset")?.Value);
    }

    [Fact]
    public void EveryShadowThatAStateOrTriggerChangesHasABaseShadowSoItsContainerIsBuiltWithTheView()
    {
        var findings = new List<string>();
        var checkedSetters = 0;
        foreach (var path in RepositoryPaths.MobileSources("*.xaml"))
        {
            var document = XDocument.Load(path, LoadOptions.SetLineInfo);
            var styles = document.Descendants(_maui + "Style")
                .Where(style => style.Attribute(_xaml + "Key") is not null)
                .ToDictionary(style => style.Attribute(_xaml + "Key")!.Value);
            foreach (var setter in document.Descendants(_maui + "Setter").Where(setter => setter.Attribute("Property")?.Value == "Shadow" && IsStateOrTrigger(setter)))
            {
                checkedSetters++;
                var style = setter.Ancestors(_maui + "Style").FirstOrDefault();
                var hasBase = style is not null
                    ? BaseShadow(style, styles) is { } styleBase && !IsNull(styleBase)
                    : Owner(setter)?.Attribute("Shadow")?.Value is { } elementBase && !IsNull(elementBase);
                if (!hasBase)
                {
                    findings.Add($"{RepositoryPaths.ToRelative(path)}:{((System.Xml.IXmlLineInfo)setter).LineNumber}");
                }
            }
        }

        Assert.True(checkedSetters >= 3, $"only {checkedSetters} state or trigger shadow setters were found");
        Assert.Empty(findings);
    }

    [Fact]
    public void NoCodeClearsOrNullsAShadowAndEveryShadowKeyItUsesIsAThemeShadow()
    {
        var shadows = Load(StylesFile).Root!.Elements(_maui + "Shadow").Select(shadow => shadow.Attribute(_xaml + "Key")!.Value).ToHashSet(StringComparer.Ordinal);
        var findings = new List<string>();
        var keys = 0;
        foreach (var path in RepositoryPaths.MobileSources("*.cs"))
        {
            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                if (NullShadow().IsMatch(lines[index]))
                {
                    findings.Add($"{RepositoryPaths.ToRelative(path)}:{index + 1}");
                }

                foreach (Match key in ShadowKey().Matches(lines[index]))
                {
                    keys++;
                    if (!shadows.Contains(key.Groups[1].Value))
                    {
                        findings.Add($"{RepositoryPaths.ToRelative(path)}:{index + 1} {key.Groups[1].Value}");
                    }
                }
            }
        }

        Assert.True(keys >= 10, $"only {keys} shadow keys were found in code");
        Assert.Empty(findings);
    }

    private static IEnumerable<(XElement Element, string Value)> ShadowValues(XDocument document)
    {
        foreach (var setter in document.Descendants().Where(element => element.Name.LocalName == "Setter" && element.Attribute("Property")?.Value == "Shadow"))
        {
            yield return (setter, setter.Attribute("Value")?.Value ?? string.Empty);
        }

        foreach (var attribute in document.Descendants().Attributes("Shadow"))
        {
            yield return (attribute.Parent!, attribute.Value);
        }
    }

    private static bool IsNull(string value) => value.Replace(" ", string.Empty, StringComparison.Ordinal) == "{x:Null}";

    private static bool IsStateOrTrigger(XElement setter) =>
        setter.Ancestors().Any(ancestor => ancestor.Name.LocalName is "VisualState" or "Trigger" or "DataTrigger" or "MultiTrigger" or "EventTrigger");

    private static XElement? Owner(XElement setter) =>
        setter.Ancestors()
            .FirstOrDefault(ancestor => ancestor.Name.LocalName.EndsWith(".Triggers", StringComparison.Ordinal) || ancestor.Name.LocalName == "VisualStateManager.VisualStateGroups")
            ?.Parent;

    private static string? StyleShadow(XElement style) =>
        style.Elements(_maui + "Setter").FirstOrDefault(setter => setter.Attribute("Property")?.Value == "Shadow")?.Attribute("Value")?.Value;

    private static string? BaseShadow(XElement style, Dictionary<string, XElement> styles)
    {
        for (XElement? current = style; current is not null;)
        {
            if (StyleShadow(current) is { } value)
            {
                return value;
            }

            var basedOn = current.Attribute("BasedOn")?.Value;
            current = basedOn is not null && StaticKey().Match(basedOn) is { Success: true } match && styles.TryGetValue(match.Groups[1].Value, out var parent) ? parent : null;
        }

        return null;
    }

    private static XDocument Load(string relative) =>
        XDocument.Load(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]), LoadOptions.SetLineInfo);

    [GeneratedRegex(@"ShadowProperty\s*,\s*null\b|\.Shadow\s*=\s*null\b|ClearValue\([^)]*ShadowProperty")]
    private static partial Regex NullShadow();

    [GeneratedRegex("\"(FsShadow\\w+)\"")]
    private static partial Regex ShadowKey();

    [GeneratedRegex(@"\{StaticResource\s+(\w+)\}")]
    private static partial Regex StaticKey();
}
