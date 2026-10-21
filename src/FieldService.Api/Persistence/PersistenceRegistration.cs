using FieldService.Api.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FieldService.Api.Persistence;

public static class PersistenceRegistration
{
    public const string Schema = "fieldservice";

    public const string ConnectionStringName = "FieldService";

    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
        services.AddOptions<DatabaseOptions>()
            .Configure(options => options.ConnectionString = configuration.GetConnectionString(ConnectionStringName))
            .ValidateOnStart();

        services.AddDbContext<FieldServiceDbContext>((provider, options) =>
            options.UseFieldServiceDatabase(
                provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString ?? string.Empty));
        services.AddDemoDataSeeding(configuration);

        return services;
    }

    public static DbContextOptionsBuilder UseFieldServiceDatabase(
        this DbContextOptionsBuilder options,
        string connectionString)
    {
        return options
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.SetPostgresVersion(17, 0);
                npgsql.MigrationsHistoryTable(MigrationsHistoryTable, Schema);
            })
            .UseSnakeCaseNamingConvention();
    }

    public static async Task PrepareDatabaseAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
        {
            await PendingMigrationsCheck.RunAsync(app.Services, cancellationToken);
        }

        await DemoDataSeeder.RunAsync(app.Services, cancellationToken);
    }
}
