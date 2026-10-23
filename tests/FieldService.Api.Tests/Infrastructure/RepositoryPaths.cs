namespace FieldService.Api.Tests.Infrastructure;

public static class RepositoryPaths
{
    private const string SolutionFileName = "FieldService.slnx";

    private static readonly Lazy<string> _root = new(FindRoot);

    public static string Root => _root.Value;

    public static string BootstrapScript => Path.Combine(Root, "infrastructure", "postgres", "bootstrap.sql");

    public static string MigrationsDirectory =>
        Path.Combine(Root, "src", "FieldService.Api", "Persistence", "Migrations");

    public static string EventContractsDirectory => Path.Combine(Root, "docs", "contracts", "events");

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"No folder above {AppContext.BaseDirectory} contains {SolutionFileName}, so the repository root is unknown.");
    }
}
