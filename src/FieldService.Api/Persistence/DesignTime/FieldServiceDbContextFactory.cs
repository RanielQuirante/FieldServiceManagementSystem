using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FieldService.Api.Persistence.DesignTime;

public sealed class FieldServiceDbContextFactory : IDesignTimeDbContextFactory<FieldServiceDbContext>
{
    public const string ConnectionStringName = "Migrations";

    public FieldServiceDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<FieldServiceDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException("ConnectionStrings:Migrations is not set.");

        var options = new DbContextOptionsBuilder<FieldServiceDbContext>();
        options.UseFieldServiceDatabase(connectionString);
        return new FieldServiceDbContext(options.Options);
    }
}
