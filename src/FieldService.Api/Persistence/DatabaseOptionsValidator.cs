using FieldService.Api.Shared.Observability;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FieldService.Api.Persistence;

public sealed class DatabaseOptionsValidator(ILogger<DatabaseOptionsValidator> logger)
    : IValidateOptions<DatabaseOptions>
{
    private const string ConnectionStringKey = "ConnectionStrings:FieldService";

    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return Fail("must be set");
        }

        NpgsqlConnectionStringBuilder connection;
        try
        {
            connection = new NpgsqlConnectionStringBuilder(options.ConnectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return Fail("must be a valid PostgreSQL connection string");
        }

        if (connection.IncludeErrorDetail)
        {
            return Fail("must not enable Include Error Detail");
        }

        return ValidateOptionsResult.Success;
    }

    private ValidateOptionsResult Fail(string rule)
    {
        ConfigurationLog.ConfigInvalid(logger, ConnectionStringKey, rule);
        return ValidateOptionsResult.Fail($"{ConnectionStringKey}: {rule}.");
    }
}
