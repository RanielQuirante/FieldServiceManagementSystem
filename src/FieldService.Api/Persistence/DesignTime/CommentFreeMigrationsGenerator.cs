using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace FieldService.Api.Persistence.DesignTime;

public sealed class CommentFreeMigrationsGenerator(
    MigrationsCodeGeneratorDependencies dependencies,
    CSharpMigrationsGeneratorDependencies csharpDependencies)
    : CSharpMigrationsGenerator(dependencies, csharpDependencies)
{
    private const string CommentMarker = "//";

    private const string PragmaDirective = "#pragma";

    public override string GenerateMigration(
        string? migrationNamespace,
        string migrationName,
        IReadOnlyList<MigrationOperation> upOperations,
        IReadOnlyList<MigrationOperation> downOperations)
    {
        return Strip(base.GenerateMigration(migrationNamespace, migrationName, upOperations, downOperations));
    }

    public override string GenerateMetadata(
        string? migrationNamespace,
        Type contextType,
        string migrationName,
        string migrationId,
        IModel targetModel)
    {
        return Strip(base.GenerateMetadata(migrationNamespace, contextType, migrationName, migrationId, targetModel));
    }

    public override string GenerateSnapshot(
        string? modelSnapshotNamespace,
        Type contextType,
        string modelSnapshotName,
        IModel model)
    {
        return Strip(base.GenerateSnapshot(modelSnapshotNamespace, contextType, modelSnapshotName, model));
    }

    private static string Strip(string code)
    {
        var kept = new List<string>();
        foreach (var line in code.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith(CommentMarker, StringComparison.Ordinal))
            {
                continue;
            }

            if (trimmed.StartsWith(PragmaDirective, StringComparison.Ordinal)
                && line.Contains(CommentMarker, StringComparison.Ordinal))
            {
                kept.Add(line[..line.IndexOf(CommentMarker, StringComparison.Ordinal)].TrimEnd());
                continue;
            }

            kept.Add(line);
        }

        return string.Join('\n', kept);
    }
}
