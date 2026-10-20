using FieldService.Api.Shared.Observability;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Persistence;

public static class PendingMigrationsCheck
{
    public const string ApplyCommand = "dotnet ef database update --project src/FieldService.Api";

    private const string ConnectionStringKey = "ConnectionStrings:FieldService";

    public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<FieldServiceDbContext>().Database;
        var pending = (await database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        if (pending.Length == 0)
        {
            return;
        }

        var names = string.Join(", ", pending);
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(PendingMigrationsCheck));
        ConfigurationLog.ConfigInvalid(
            logger,
            ConnectionStringKey,
            $"the database must have no pending migrations; pending: {names}; apply them with {ApplyCommand}");

        throw new InvalidOperationException(
            $"Pending migrations: {names}. Apply them with {ApplyCommand} (VS Code task db: migrate).");
    }
}
