namespace FieldService.Mobile.Tests.Shared.Testing;

public static class RepositoryPaths
{
    private const string SolutionFileName = "FieldService.slnx";

    private static readonly Lazy<string> _root = new(FindRoot);

    public static string RepositoryRoot => _root.Value;

    public static string MobileApp => Path.Combine(RepositoryRoot, "src", "FieldService.Mobile");

    public static string ToRelative(string path) => Path.GetRelativePath(MobileApp, path).Replace('\\', '/');

    public static IReadOnlyList<string> MobileSources(string pattern) =>
        [.. Directory
            .EnumerateFiles(MobileApp, pattern, SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(ToRelative(path)))
            .Order(StringComparer.Ordinal)];

    private static bool IsBuildOutput(string relativePath) =>
        relativePath.StartsWith("bin/", StringComparison.Ordinal) || relativePath.StartsWith("obj/", StringComparison.Ordinal);

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"{SolutionFileName} was not found above {AppContext.BaseDirectory}.");
    }
}
