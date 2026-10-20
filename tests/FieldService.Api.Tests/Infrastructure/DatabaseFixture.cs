using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FieldService.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Respawn;
using Respawn.Graph;

namespace FieldService.Api.Tests.Infrastructure;

public sealed partial class DatabaseFixture : IAsyncLifetime
{
    public const string AdminConnectionKey = "FIELDSERVICE_TEST_ADMIN_CONNECTION";

    public const string MigratorPasswordKey = "FIELDSERVICE_MIGRATOR_PASSWORD";

    public const string AppPasswordKey = "FIELDSERVICE_APP_PASSWORD";

    public const string UserSecretsId = "fieldservice-api-tests";

    public const string MigratorRole = "fieldservice_migrator";

    public const string AppRole = "fieldservice_app";

    public const string RunDatabasePrefix = "fieldservice_test_";

    private const string BootstrapScriptName = "infrastructure/postgres/bootstrap.sql";

    private const int MinimumPasswordLength = 16;

    private const int FirstSupportedServerVersion = 170000;

    private const int FirstUnsupportedServerVersion = 180000;

    private const int MinimumPsqlMajorVersion = 15;

    private const int ServerCheckTimeoutSeconds = 3;

    private const int AdminConnectTimeoutSeconds = 15;

    private const int AppMaxPoolSize = 20;

    private static readonly TimeSpan _leftoverAge = TimeSpan.FromHours(24);

    private static readonly TimeSpan _processTimeout = TimeSpan.FromMinutes(2);

    private static readonly string[] _keptTables =
    [
        PersistenceRegistration.MigrationsHistoryTable,
        "checklist_templates",
        "checklist_items",
        "checklist_reading_fields",
    ];

    private ConnectionSettings? _settings;

    private Respawner? _respawner;

    private ApiFactory? _factory;

    public string DatabaseName { get; private set; } = string.Empty;

    public string AppConnectionString { get; private set; } = string.Empty;

    public string MigratorConnectionString { get; private set; } = string.Empty;

    public JobNumberSequenceState InitialJobNumberSequence { get; private set; }

    public ApiFactory Factory => _factory ?? throw NotInitialized();

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var settings = ConnectionSettings.Load(ReadConfiguration());
        await CheckServerAsync(settings, cancellationToken);
        await DropLeftoverDatabasesAsync(settings, cancellationToken);
        await CheckPsqlAsync(cancellationToken);

        _settings = settings;
        DatabaseName = NewRunDatabaseName();
        MigratorConnectionString = settings.RunConnectionString(
            DatabaseName, MigratorRole, settings.MigratorPassword, "fieldservice-api-tests-migrator", maxPoolSize: null);
        AppConnectionString = settings.RunConnectionString(
            DatabaseName, AppRole, settings.AppPassword, "fieldservice-api-tests", AppMaxPoolSize);

        try
        {
            await RunBootstrapAsync(settings, cancellationToken);
            await MigrateAsync(cancellationToken);
            InitialJobNumberSequence = await ReadJobNumberSequenceAsync(cancellationToken);
            _respawner = await CreateRespawnerAsync(cancellationToken);
            _factory = new ApiFactory(AppConnectionString);
        }
        catch
        {
            await DropRunDatabaseAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await DropRunDatabaseAsync();
    }

    public ApiFactory CreateFactory(Action<ApiFactory>? configure = null)
    {
        _ = _settings ?? throw NotInitialized();
        var factory = new ApiFactory(AppConnectionString);
        configure?.Invoke(factory);
        return factory;
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        var respawner = _respawner ?? throw NotInitialized();
        await using var connection = await OpenMigratorConnectionAsync(cancellationToken);
        await respawner.ResetAsync(connection);
        Factory.LogSink.Clear();
    }

    public FieldServiceDbContext CreateAppDbContext()
    {
        return CreateDbContext(AppConnectionString);
    }

    public FieldServiceDbContext CreateMigratorDbContext()
    {
        return CreateDbContext(MigratorConnectionString);
    }

    public Task<NpgsqlConnection> OpenAppConnectionAsync(CancellationToken cancellationToken)
    {
        return OpenAsync(AppConnectionString, cancellationToken);
    }

    public Task<NpgsqlConnection> OpenMigratorConnectionAsync(CancellationToken cancellationToken)
    {
        return OpenAsync(MigratorConnectionString, cancellationToken);
    }

    private static IConfiguration ReadConfiguration()
    {
        return new ConfigurationBuilder()
            .AddUserSecrets(typeof(DatabaseFixture).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    private static FieldServiceDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<FieldServiceDbContext>();
        options.UseFieldServiceDatabase(connectionString);
        return new FieldServiceDbContext(options.Options);
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task CheckServerAsync(ConnectionSettings settings, CancellationToken cancellationToken)
    {
        int versionNumber;
        string version;
        try
        {
            await using var connection = await OpenAsync(
                settings.AdminConnectionString(ServerCheckTimeoutSeconds), cancellationToken);
            await using var command = new NpgsqlCommand(
                "SELECT current_setting('server_version_num')::integer, current_setting('server_version')",
                connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            versionNumber = reader.GetInt32(0);
            version = reader.GetString(1);
        }
        catch (PostgresException exception)
        {
            throw new InvalidOperationException(
                $"PostgreSQL at {settings.Host}:{settings.Port} refused the admin connection as {settings.User} ({AdminConnectionKey}): {exception.SqlState} {exception.MessageText}",
                exception);
        }
        catch (Exception exception) when (exception is NpgsqlException or SocketException or TimeoutException)
        {
            throw new InvalidOperationException(
                $"PostgreSQL is not reachable at {settings.Host}:{settings.Port} as {settings.User} ({AdminConnectionKey}). Start PostgreSQL 17 or correct the variable.",
                exception);
        }

        if (versionNumber is < FirstSupportedServerVersion or >= FirstUnsupportedServerVersion)
        {
            throw new InvalidOperationException($"PostgreSQL 17 is required for tests; the server reports {version}.");
        }
    }

    private static async Task DropLeftoverDatabasesAsync(ConnectionSettings settings, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(
            settings.AdminConnectionString(AdminConnectTimeoutSeconds), cancellationToken);
        var leftovers = new List<string>();
        await using (var command = new NpgsqlCommand(
            """SELECT datname FROM pg_database WHERE datname LIKE 'fieldservice\_test\_%'""",
            connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(0);
                if (IsLeftover(name, DateTimeOffset.UtcNow))
                {
                    leftovers.Add(name);
                }
            }
        }

        foreach (var name in leftovers)
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static bool IsLeftover(string name, DateTimeOffset now)
    {
        var match = RunDatabasePattern().Match(name);
        return match.Success
            && DateTimeOffset.TryParseExact(
                match.Groups["stamp"].Value,
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var created)
            && now - created > _leftoverAge;
    }

    private static string NewRunDatabaseName()
    {
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        return $"{RunDatabasePrefix}{stamp}_{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4))}";
    }

    private static async Task CheckPsqlAsync(CancellationToken cancellationToken)
    {
        var message = $"psql {MinimumPsqlMajorVersion} or later must be on PATH to run {BootstrapScriptName}.";
        ProcessResult result;
        try
        {
            result = await RunProcessAsync("psql", ["--version"], environment: null, cancellationToken);
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(message, exception);
        }

        var match = PsqlVersionPattern().Match(result.StandardOutput);
        if (result.ExitCode != 0
            || !match.Success
            || int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture) < MinimumPsqlMajorVersion)
        {
            throw new InvalidOperationException(message);
        }
    }

    private async Task RunBootstrapAsync(ConnectionSettings settings, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(
            "psql",
            ["-X", "-w", "-q", "-v", "ON_ERROR_STOP=1", "-v", $"db_name={DatabaseName}", "-f", RepositoryPaths.BootstrapScript],
            settings.PsqlEnvironment(),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            var errors = result.StandardError
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Contains("ERROR:", StringComparison.Ordinal) || line.Contains("FATAL:", StringComparison.Ordinal));
            throw new InvalidOperationException(
                $"psql exited with code {result.ExitCode} while running {BootstrapScriptName} for {DatabaseName}: {string.Join(" | ", errors)}");
        }
    }

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var context = CreateMigratorDbContext();
        await context.Database.MigrateAsync(cancellationToken);
    }

    private async Task<JobNumberSequenceState> ReadJobNumberSequenceAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenMigratorConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT last_value, is_called FROM fieldservice.jobs_number_seq",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new JobNumberSequenceState(reader.GetInt64(0), reader.GetBoolean(1));
    }

    private async Task<Respawner> CreateRespawnerAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenMigratorConnectionAsync(cancellationToken);
        return await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = [PersistenceRegistration.Schema],
            TablesToIgnore = [.. _keptTables.Select(table => new Table(PersistenceRegistration.Schema, table))],
        });
    }

    private async Task DropRunDatabaseAsync()
    {
        NpgsqlConnection.ClearAllPools();
        if (_settings is null || !RunDatabasePattern().IsMatch(DatabaseName))
        {
            return;
        }

        await using var connection = await OpenAsync(
            _settings.AdminConnectionString(AdminConnectTimeoutSeconds), CancellationToken.None);
        await using (var drop = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)",
            connection))
        {
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await using var check = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = @name", connection);
        check.Parameters.AddWithValue("name", DatabaseName);
        if ((long)(await check.ExecuteScalarAsync(CancellationToken.None))! != 0)
        {
            throw new InvalidOperationException($"The run database {DatabaseName} still exists after DROP DATABASE.");
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?>? environment,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment ?? new Dictionary<string, string?>())
        {
            if (value is null)
            {
                start.Environment.Remove(name);
            }
            else
            {
                start.Environment[name] = value;
            }
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"{fileName} did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_processTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{fileName} did not finish within {_processTimeout.TotalSeconds} seconds.");
        }

        return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
    }

    private static InvalidOperationException NotInitialized()
    {
        return new InvalidOperationException("The database fixture is not initialized.");
    }

    [GeneratedRegex(@"^fieldservice_test_(?<stamp>\d{14})_[0-9a-f]{8}$")]
    private static partial Regex RunDatabasePattern();

    [GeneratedRegex(@"\(PostgreSQL\) (?<major>\d+)")]
    private static partial Regex PsqlVersionPattern();

    public readonly record struct JobNumberSequenceState(long LastValue, bool IsCalled);

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class ConnectionSettings
    {
        private const string AdminDefaultDatabase = "postgres";

        private ConnectionSettings(NpgsqlConnectionStringBuilder admin, string migratorPassword, string appPassword)
        {
            Admin = admin;
            MigratorPassword = migratorPassword;
            AppPassword = appPassword;
        }

        public string Host => Admin.Host!;

        public int Port => Admin.Port;

        public string User => string.IsNullOrEmpty(Admin.Username) ? Environment.UserName : Admin.Username;

        public string MigratorPassword { get; }

        public string AppPassword { get; }

        private NpgsqlConnectionStringBuilder Admin { get; }

        public static ConnectionSettings Load(IConfiguration configuration)
        {
            var adminConnection = configuration[AdminConnectionKey];
            if (string.IsNullOrWhiteSpace(adminConnection))
            {
                throw new InvalidOperationException(
                    $"{AdminConnectionKey} is set neither in the environment nor in user-secrets {UserSecretsId}. Run node infrastructure/scripts/create-dev-env.mjs, or export {AdminConnectionKey}=\"Host=127.0.0.1;Port=5432;Database=postgres;Username=$USER\". See docs/testing.md.");
            }

            NpgsqlConnectionStringBuilder admin;
            try
            {
                admin = new NpgsqlConnectionStringBuilder(adminConnection);
            }
            catch (ArgumentException)
            {
                throw new InvalidOperationException(
                    $"{AdminConnectionKey} is not a valid PostgreSQL connection string. See docs/testing.md.");
            }

            if (string.IsNullOrWhiteSpace(admin.Host))
            {
                throw new InvalidOperationException($"{AdminConnectionKey} names no Host. See docs/testing.md.");
            }

            var migratorPassword = configuration[MigratorPasswordKey];
            var appPassword = configuration[AppPasswordKey];
            if (migratorPassword is null
                || appPassword is null
                || migratorPassword.Length < MinimumPasswordLength
                || appPassword.Length < MinimumPasswordLength)
            {
                throw new InvalidOperationException(
                    $"{MigratorPasswordKey} and {AppPasswordKey} must each be set, with at least {MinimumPasswordLength} characters, in the environment or in user-secrets {UserSecretsId}. Run node infrastructure/scripts/create-dev-env.mjs. See docs/testing.md.");
            }

            return new ConnectionSettings(admin, migratorPassword, appPassword);
        }

        public string AdminConnectionString(int timeoutSeconds)
        {
            return new NpgsqlConnectionStringBuilder(Admin.ConnectionString)
            {
                Database = string.IsNullOrEmpty(Admin.Database) ? AdminDefaultDatabase : Admin.Database,
                Timeout = timeoutSeconds,
                Pooling = false,
                ApplicationName = "fieldservice-api-tests-admin",
            }.ConnectionString;
        }

        public string RunConnectionString(
            string database,
            string role,
            string password,
            string applicationName,
            int? maxPoolSize)
        {
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = Admin.Host,
                Port = Admin.Port,
                Database = database,
                Username = role,
                Password = password,
                ApplicationName = applicationName,
                SslMode = Admin.SslMode,
            };
            if (maxPoolSize is { } size)
            {
                builder.MaxPoolSize = size;
            }

            return builder.ConnectionString;
        }

        public Dictionary<string, string?> PsqlEnvironment()
        {
            return new Dictionary<string, string?>
            {
                ["PGHOST"] = Host,
                ["PGPORT"] = Port.ToString(CultureInfo.InvariantCulture),
                ["PGUSER"] = User,
                ["PGDATABASE"] = string.IsNullOrEmpty(Admin.Database) ? AdminDefaultDatabase : Admin.Database,
                ["PGPASSWORD"] = string.IsNullOrEmpty(Admin.Password) ? null : Admin.Password,
                ["PGSSLMODE"] = SslModeName(Admin.SslMode),
                ["PGCONNECT_TIMEOUT"] = AdminConnectTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                ["PGSERVICE"] = null,
                ["PGOPTIONS"] = null,
                [MigratorPasswordKey] = MigratorPassword,
                [AppPasswordKey] = AppPassword,
            };
        }

        private static string SslModeName(SslMode mode) => mode switch
        {
            SslMode.Disable => "disable",
            SslMode.Allow => "allow",
            SslMode.Require => "require",
            SslMode.VerifyCA => "verify-ca",
            SslMode.VerifyFull => "verify-full",
            _ => "prefer",
        };
    }
}
