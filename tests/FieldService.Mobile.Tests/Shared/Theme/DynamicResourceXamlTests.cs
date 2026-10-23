using System.Text.RegularExpressions;
using System.Xml.Linq;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed partial class DynamicResourceXamlTests
{
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2009/xaml";

    [Fact]
    public void NoNamedElementSetsInXamlAPropertyItsCodeBehindSetsAsADynamicResource()
    {
        var findings = new List<string>();
        var checkedPairs = 0;
        foreach (var path in RepositoryPaths.MobileSources("*.xaml.cs"))
        {
            var xamlPath = path[..^".cs".Length];
            if (!File.Exists(xamlPath))
            {
                continue;
            }

            var named = XDocument.Load(xamlPath).Descendants()
                .Where(element => element.Attribute(_xaml + "Name") is not null && !element.Name.LocalName.StartsWith("VisualState", StringComparison.Ordinal))
                .ToDictionary(element => element.Attribute(_xaml + "Name")!.Value, StringComparer.Ordinal);
            foreach (Match match in DynamicResourceCall().Matches(File.ReadAllText(path)))
            {
                if (!named.TryGetValue(match.Groups["name"].Value, out var element))
                {
                    continue;
                }

                checkedPairs++;
                var property = match.Groups["property"].Value;
                if (element.Attribute(property) is not null)
                {
                    findings.Add($"{RepositoryPaths.ToRelative(xamlPath)} {match.Groups["name"].Value}.{property}");
                }
            }
        }

        Assert.True(checkedPairs >= 40, $"{checkedPairs} pairs checked");
        Assert.Empty(findings);
    }

    [GeneratedRegex(@"(?<name>[A-Z]\w*)\.SetDynamicResource\((?:[\w.]+\.)?(?<property>\w+)Property\b")]
    private static partial Regex DynamicResourceCall();
}
