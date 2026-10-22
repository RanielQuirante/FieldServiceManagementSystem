using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Build;

public sealed partial class AndroidResourceDesignerTests
{
    private const string IsolationTarget = "IsolateDesignTimeResourceDesigner";
    private const string SetupTarget = "_SetupDesignerProperties";
    private const string DesignerPathProperty = "_DesignerIntermediateOutputPath";
    private const string DesignTimeFolder = @"$(IntermediateOutputPath)designtime\";
    private const string DesignerTargetsFile = "Xamarin.Android.Resource.Designer.targets";

    [Fact]
    public void DesignTimeBuildsWriteTheResourceDesignerToTheirOwnFolder()
    {
        var project = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "FieldService.Mobile.csproj"));

        var target = Assert.Single(project.Root!.Elements("Target"), element => element.Attribute("Name")?.Value == IsolationTarget);

        Assert.Equal(SetupTarget, target.Attribute("BeforeTargets")?.Value);
        Assert.Contains("'$(DesignTimeBuild)' == 'true'", target.Attribute("Condition")?.Value, StringComparison.Ordinal);
        Assert.Contains("'$(AndroidUseDesignerAssembly)' == 'true'", target.Attribute("Condition")?.Value, StringComparison.Ordinal);
        var path = Assert.Single(target.Elements("PropertyGroup").Elements(DesignerPathProperty));
        Assert.Equal(DesignTimeFolder, path.Value);
    }

    [Fact]
    public void TheInstalledAndroidSdkKeepsADesignerFolderSetBeforeItsSetup()
    {
        var targetsFile = NewestAndroidSdkDesignerTargets();
        if (targetsFile is null)
        {
            Assert.Skip("No Android SDK pack is installed beside this .NET runtime.");
            return;
        }

        var targets = XDocument.Load(targetsFile).Root!.Elements().Where(element => element.Name.LocalName == "Target").ToList();
        var setup = Assert.Single(targets, target => target.Attribute("Name")?.Value == SetupTarget);
        var assignments = setup.Descendants().Where(element => element.Name.LocalName == DesignerPathProperty).ToList();

        Assert.NotEmpty(assignments);
        Assert.All(assignments, assignment => Assert.StartsWith("'$(_DesignerIntermediateOutputPath)' == ''", assignment.Attribute("Condition")?.Value?.Trim(), StringComparison.Ordinal));
        foreach (var name in new[] { "_GenerateRtxt", "_GenerateResourceDesignerAssembly" })
        {
            var target = Assert.Single(targets, candidate => candidate.Attribute("Name")?.Value == name);
            Assert.Contains("$(_DesignerIntermediateOutputPath)R.txt", target.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheResourceIdsOfALocalAndroidBuildComeFromAapt2()
    {
        var files = new[] { "Debug", "Release" }
            .Select(configuration => Path.Combine(RepositoryPaths.MobileApp, "obj", configuration, "net10.0-android", "R.txt"))
            .Where(File.Exists)
            .ToList();
        if (files.Count == 0)
        {
            Assert.Skip("No local Android build of the app.");
            return;
        }

        var findings = files
            .Where(file => File.ReadLines(file).Any(line => ManagedParserStyle().IsMatch(line)))
            .Select(RepositoryPaths.ToRelative)
            .ToList();

        Assert.True(findings.Count == 0, "R.txt written by a design-time build; rebuild the Android head with --no-incremental: " + string.Join(", ", findings));
    }

    private static string? NewestAndroidSdkDesignerTargets()
    {
        var root = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        var packs = Path.Combine(root, "packs");
        if (!Directory.Exists(packs))
        {
            return null;
        }

        return Directory.EnumerateDirectories(packs, "Microsoft.Android.Sdk.*")
            .SelectMany(Directory.EnumerateDirectories)
            .Select(directory => (Directory: directory, Version: Version.TryParse(Path.GetFileName(directory), out var version) ? version : null))
            .Where(candidate => candidate.Version is not null && File.Exists(Path.Combine(candidate.Directory, "tools", DesignerTargetsFile)))
            .OrderByDescending(candidate => candidate.Version)
            .Select(candidate => Path.Combine(candidate.Directory, "tools", DesignerTargetsFile))
            .FirstOrDefault();
    }

    [GeneratedRegex(@"^int style \S*\.\S* 0x")]
    private static partial Regex ManagedParserStyle();
}
