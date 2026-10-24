using System.Buffers.Text;
using System.Security.Cryptography;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog.Events;

namespace FieldService.Api.Tests.Security;

[Collection(DatabaseCollection.Name)]
public sealed class StartupValidationTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string ConfigInvalidEvent = "config.invalid";

    private const string AccessKey = "Auth:AccessTokenKey";

    private const string SocketKey = "Auth:SocketTicketKey";

    private const string WebhookSecret = "Notifications:WebhookSecret";

    private const string BaseUrl = "Notifications:BaseUrl";

    private const string Origins = "Cors:AllowedOrigins:0";

    private const string ConnectionString = "ConnectionStrings:FieldService";

    private const string KnownProxies = ApiFactory.KnownProxiesKey;

    private const string FirstKnownProxy = $"{ApiFactory.KnownProxiesKey}:0";

    private const string ProxyAddressRule = "must be an IP address of one host";

    private const string ProxyRequiredRule = "must list at least one proxy address outside Development and Testing";

    private const string PublishedRule = "published test keys";

    private const string PatternedRule = "patterned key";

    private const string KeyFormatRule = "standard base64 of 32 or more";

    private const string WebhookFormatRule = "whsec_ followed by standard base64";

    private static readonly string[] _allowedEventProperties = ["Key", "Rule", "EventId", "SourceContext"];

    public static TheoryData<string> RefusedSettings =>
    [
        "connection string missing",
        "connection string blank",
        "connection string malformed",
        "connection string with Include Error Detail",
        .. KeyCases(AccessKey),
        .. KeyCases(SocketKey),
        "socket-ticket key equal to the access-token key",
        "webhook secret without the whsec_ prefix",
        "webhook secret with a body that is not base64",
        "webhook secret of 31 bytes",
        "webhook secret published 0x00 in whsec_ form",
        "webhook secret published 0x20 in whsec_ form",
        "webhook secret published 0x40 in whsec_ form",
        "webhook secret with all-equal bytes",
        "webhook secret with a constant step",
        "webhook secret equal to the access-token key",
        "webhook secret equal to the socket-ticket key",
        "base URL that is not absolute",
        "base URL with another scheme",
        "base URL set without a webhook secret",
        "http base URL in Production",
        "CORS origin *",
        "CORS origin with a path",
        "CORS origin with a trailing slash",
        "CORS origin without a scheme",
        "CORS origin with another scheme",
        "http CORS origin in Production",
        "known proxy that is not an address",
        "no known proxy in Staging",
        "no known proxy in Production",
        "known proxy with the unspecified IPv4 address",
        "known proxy with the unspecified IPv6 address",
        "known proxy with the IPv4 broadcast address",
        "known proxy with an IPv4 multicast address",
        "known proxy with an IPv6 multicast address",
        "known proxy in shorthand IPv4 form",
        "known network that does not parse",
        "known network in Staging",
        "known network in Production",
        "demo seeding without a password",
        "demo seeding with an 11-character password",
        "demo seeding in Production",
    ];

    public static TheoryData<string> AllowedHostsRefusedInProduction =>
    [
        "*",
        "(empty)",
        " ; ",
        "<sentinel>;*",
        "*;<sentinel>",
    ];

    public static TheoryData<string, string> AcceptedSettings => new()
    {
        { "Production", "AllowedHosts=<sentinel>" },
        { "Production", "AllowedHosts=<sentinel>;localhost" },
        { "Testing", "AllowedHosts=*" },
        { "Development", "ForwardedHeaders:KnownNetworks:0=172.30.80.0/24" },
        { "Testing", "ForwardedHeaders:KnownNetworks:0=172.30.80.0/24" },
        { "Staging", "ForwardedHeaders:KnownProxies:0=172.30.80.10" },
        { "Production", "ForwardedHeaders:KnownProxies:0=172.30.80.10" },
        { "Production", "ForwardedHeaders:KnownProxies:0=2001:db8::10" },
        { "Testing", "Notifications:BaseUrl=http://127.0.0.1:5090" },
        { "Testing", "Auth:AccessTokenKey=<64 random bytes>" },
    };

    [Theory]
    [MemberData(nameof(RefusedSettings))]
    public async Task EachInvalidSettingStopsTheHostAndLogsOnlyTheKeyAndTheRule(string setting)
    {
        var refused = CaseFor(setting);

        var result = await StartAsync(refused.Environment, factory => refused.Apply(factory));

        AssertRefused(result, refused.Key, refused.RuleFragment, refused.Sentinels(result.Factory));
    }

    [Theory]
    [MemberData(nameof(AllowedHostsRefusedInProduction))]
    public async Task ProductionRefusesAnAllowedHostsListThatIsEmptyOrHoldsAWildcard(string allowedHosts)
    {
        var sentinel = $"host-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}.example";
        var value = allowedHosts == "(empty)" ? string.Empty : allowedHosts.Replace("<sentinel>", sentinel, StringComparison.Ordinal);

        var result = await StartAsync("Production", factory => factory.Settings["AllowedHosts"] = value);

        AssertRefused(result, "AllowedHosts", "must list the public host names in Production", [sentinel]);
    }

    [Theory]
    [MemberData(nameof(AcceptedSettings))]
    public async Task AValidSettingLetsTheHostStartWithoutAConfigInvalidEvent(string environment, string setting)
    {
        var sentinel = $"host-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}.example";
        var pair = setting.Split('=', 2);
        var value = pair[1] == "<64 random bytes>"
            ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
            : pair[1].Replace("<sentinel>", sentinel, StringComparison.Ordinal);

        var result = await StartAsync(environment, factory =>
        {
            factory.Settings[pair[0]] = value;
            if (pair[0] == BaseUrl)
            {
                factory.Settings[WebhookSecret] = SecretKeyRules.WebhookSecretPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            }
        });

        Assert.Null(result.Failure);
        Assert.Empty(result.ConfigInvalid);
    }

    [Fact]
    public async Task OneStartReportsEveryInvalidKeyAcrossValidators()
    {
        var shortKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(31));
        var malformedSecret = $"{SecretKeyRules.WebhookSecretPrefix}{Marker()}!!";

        var result = await StartAsync("Testing", factory =>
        {
            factory.Settings[AccessKey] = shortKey;
            factory.Settings[Origins] = "*";
            factory.Settings[WebhookSecret] = malformedSecret;
        });

        Assert.NotNull(result.Failure);
        var aggregate = Assert.IsType<AggregateException>(result.Failure);
        Assert.Equal(3, aggregate.InnerExceptions.Count);
        Assert.All(aggregate.InnerExceptions, inner => Assert.IsType<OptionsValidationException>(inner));
        Assert.Equal(
            new[] { AccessKey, "Cors:AllowedOrigins", WebhookSecret }.Order(StringComparer.Ordinal),
            result.ConfigInvalid.Select(logEvent => CapturingLogSink.Scalar(logEvent, "Key")).Order(StringComparer.Ordinal));
        AssertNoValue(result, [shortKey, malformedSecret]);
    }

    [Fact]
    public async Task OneStartReportsBothInvalidKeysOfOneValidator()
    {
        var shortKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var patternedKey = Convert.ToBase64String(Run(0x91, 32, step: 5));

        var result = await StartAsync("Testing", factory =>
        {
            factory.Settings[AccessKey] = shortKey;
            factory.Settings[SocketKey] = patternedKey;
        });

        var failure = Assert.IsType<OptionsValidationException>(result.Failure);
        Assert.Equal(2, failure.Failures.Count());
        Assert.Contains(failure.Failures, message => message.StartsWith($"{AccessKey}: ", StringComparison.Ordinal));
        Assert.Contains(failure.Failures, message => message.StartsWith($"{SocketKey}: ", StringComparison.Ordinal));
        Assert.Equal(
            new[] { AccessKey, SocketKey },
            result.ConfigInvalid.Select(logEvent => CapturingLogSink.Scalar(logEvent, "Key")).Order(StringComparer.Ordinal));
        AssertNoValue(result, [shortKey, patternedKey]);
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Staging", true)]
    [InlineData("Testing", false)]
    public async Task PendingMigrationsStopTheHostInDevelopmentAndStagingOnly(string environment, bool refused)
    {
        await using var migrator = await Fixture.OpenMigratorConnectionAsync(CancellationToken);
        await ExecuteAsync(
            migrator,
            """
            CREATE TEMP TABLE saved_history AS SELECT * FROM fieldservice."__EFMigrationsHistory";
            DELETE FROM fieldservice."__EFMigrationsHistory";
            """);
        StartResult result;
        try
        {
            result = await StartAsync(environment, _ => { });
        }
        finally
        {
            await ExecuteAsync(
                migrator,
                """
                INSERT INTO fieldservice."__EFMigrationsHistory" SELECT * FROM saved_history;
                DROP TABLE saved_history;
                """);
        }

        if (!refused)
        {
            Assert.Null(result.Failure);
            Assert.Empty(result.ConfigInvalid);
            return;
        }

        Assert.NotNull(result.Failure);
        Assert.Contains(Flatten(result.Failure), exception =>
            exception is InvalidOperationException && exception.Message.Contains("dotnet ef database update", StringComparison.Ordinal));
        var logged = Assert.Single(result.ConfigInvalid);
        Assert.Equal(ConnectionString, CapturingLogSink.Scalar(logged, "Key"));
        Assert.Contains("no pending migrations", CapturingLogSink.Scalar(logged, "Rule"), StringComparison.Ordinal);
        AssertNoValue(result, [Fixture.AppConnectionString, PasswordOf(Fixture.AppConnectionString)]);
    }

    private static IEnumerable<string> KeyCases(string key) =>
    [
        $"{key} missing",
        $"{key} blank",
        $"{key} not base64",
        $"{key} of 31 bytes",
        $"{key} in base64url form",
        $"{key} published 0x00",
        $"{key} published 0x20",
        $"{key} published 0x40",
        $"{key} published 0x00 in whsec_ form",
        $"{key} published 0x20 in whsec_ form",
        $"{key} published 0x40 in whsec_ form",
        $"{key} with all-equal bytes",
        $"{key} with a constant step",
        $"{key} with a descending step",
        $"{key} of 64 bytes with a constant step",
    ];

    private static RefusedCase CaseFor(string setting)
    {
        foreach (var key in new[] { AccessKey, SocketKey })
        {
            if (setting.StartsWith($"{key} ", StringComparison.Ordinal))
            {
                return KeyCase(key, setting[(key.Length + 1)..]);
            }
        }

        var randomSecret = $"{SecretKeyRules.WebhookSecretPrefix}{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}";
        return setting switch
        {
            "connection string missing" => Single("Testing", ConnectionString, null, ConnectionString, "must be set"),
            "connection string blank" => Single("Testing", ConnectionString, "   ", ConnectionString, "must be set"),
            "connection string malformed" => Single("Testing", ConnectionString, $"{Marker()}=;;=", ConnectionString, "valid PostgreSQL connection string"),
            "connection string with Include Error Detail" => new RefusedCase(
                "Testing",
                factory => factory.Settings[ConnectionString] = $"{factory.Settings[ConnectionString]};Include Error Detail=true",
                ConnectionString,
                "Include Error Detail",
                factory => [PasswordOf(factory.Settings[ConnectionString]!)]),
            "socket-ticket key equal to the access-token key" => new RefusedCase(
                "Testing",
                factory => factory.Settings[SocketKey] = factory.Settings[AccessKey],
                SocketKey,
                $"must differ from {AccessKey}",
                factory => [factory.Settings[AccessKey]!]),
            "webhook secret without the whsec_ prefix" =>
                Single("Testing", WebhookSecret, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), WebhookSecret, WebhookFormatRule),
            "webhook secret with a body that is not base64" =>
                Single("Testing", WebhookSecret, $"{SecretKeyRules.WebhookSecretPrefix}{Marker()}%%", WebhookSecret, WebhookFormatRule),
            "webhook secret of 31 bytes" => Single(
                "Testing",
                WebhookSecret,
                $"{SecretKeyRules.WebhookSecretPrefix}{Convert.ToBase64String(RandomNumberGenerator.GetBytes(31))}",
                WebhookSecret,
                WebhookFormatRule),
            "webhook secret published 0x00 in whsec_ form" => Single("Testing", WebhookSecret, Whsec(Run(0x00)), WebhookSecret, PublishedRule),
            "webhook secret published 0x20 in whsec_ form" => Single("Testing", WebhookSecret, Whsec(Run(0x20)), WebhookSecret, PublishedRule),
            "webhook secret published 0x40 in whsec_ form" => Single("Testing", WebhookSecret, Whsec(Run(0x40)), WebhookSecret, PublishedRule),
            "webhook secret with all-equal bytes" => Single("Testing", WebhookSecret, Whsec(Run(0x5A, 32, step: 0)), WebhookSecret, PatternedRule),
            "webhook secret with a constant step" => Single("Testing", WebhookSecret, Whsec(Run(0x07, 32, step: 3)), WebhookSecret, PatternedRule),
            "webhook secret equal to the access-token key" => new RefusedCase(
                "Testing",
                factory => factory.Settings[WebhookSecret] = SecretKeyRules.WebhookSecretPrefix + factory.Settings[AccessKey],
                WebhookSecret,
                $"must differ from {AccessKey}",
                factory => [factory.Settings[AccessKey]!]),
            "webhook secret equal to the socket-ticket key" => new RefusedCase(
                "Testing",
                factory => factory.Settings[WebhookSecret] = SecretKeyRules.WebhookSecretPrefix + factory.Settings[SocketKey],
                WebhookSecret,
                $"must differ from {SocketKey}",
                factory => [factory.Settings[SocketKey]!]),
            "base URL that is not absolute" => Single("Testing", BaseUrl, $"notifications-{Marker()}/hooks", BaseUrl, "absolute http or https URL"),
            "base URL with another scheme" => Single("Testing", BaseUrl, $"ftp://notifications-{Marker()}.example", BaseUrl, "absolute http or https URL"),
            "base URL set without a webhook secret" => new RefusedCase(
                "Testing",
                factory =>
                {
                    factory.Settings[BaseUrl] = "http://127.0.0.1:5090";
                    factory.Settings[WebhookSecret] = null;
                },
                WebhookSecret,
                $"must be set when {BaseUrl} is set",
                _ => []),
            "http base URL in Production" => new RefusedCase(
                "Production",
                factory =>
                {
                    factory.Settings[BaseUrl] = $"http://notifications-{Marker()}.example";
                    factory.Settings[WebhookSecret] = randomSecret;
                },
                BaseUrl,
                "must use https in Production",
                factory => [factory.Settings[BaseUrl]!, randomSecret]),
            "CORS origin *" => Single("Testing", Origins, "*", "Cors:AllowedOrigins", "without a path, and * is not allowed"),
            "CORS origin with a path" => Single("Testing", Origins, $"http://{Marker()}.example/app", "Cors:AllowedOrigins", "without a path"),
            "CORS origin with a trailing slash" => Single("Testing", Origins, $"http://{Marker()}.example/", "Cors:AllowedOrigins", "without a path"),
            "CORS origin without a scheme" => Single("Testing", Origins, $"{Marker()}.example:5173", "Cors:AllowedOrigins", "absolute http or https origin"),
            "CORS origin with another scheme" => Single("Testing", Origins, $"ftp://{Marker()}.example", "Cors:AllowedOrigins", "absolute http or https origin"),
            "http CORS origin in Production" => Single("Production", Origins, $"http://{Marker()}.example", "Cors:AllowedOrigins", "must use https in Production"),
            "known proxy that is not an address" =>
                Single("Testing", "ForwardedHeaders:KnownProxies:0", $"proxy-{Marker()}", "ForwardedHeaders:KnownProxies", "must be an IP address"),
            "no known proxy in Staging" => NoProxyCase("Staging"),
            "no known proxy in Production" => NoProxyCase("Production"),
            "known proxy with the unspecified IPv4 address" => Single("Staging", FirstKnownProxy, "0.0.0.0", KnownProxies, ProxyAddressRule),
            "known proxy with the unspecified IPv6 address" => Single("Production", FirstKnownProxy, "::", KnownProxies, ProxyAddressRule),
            "known proxy with the IPv4 broadcast address" => Single("Staging", FirstKnownProxy, "255.255.255.255", KnownProxies, ProxyAddressRule),
            "known proxy with an IPv4 multicast address" => Single("Production", FirstKnownProxy, "239.255.42.99", KnownProxies, ProxyAddressRule),
            "known proxy with an IPv6 multicast address" => Single("Staging", FirstKnownProxy, "ff0e::2a:63", KnownProxies, ProxyAddressRule),
            "known proxy in shorthand IPv4 form" => Single("Production", FirstKnownProxy, "10.77.200", KnownProxies, ProxyAddressRule),
            "known network that does not parse" =>
                Single("Testing", "ForwardedHeaders:KnownNetworks:0", "10.0.0.0/33", "ForwardedHeaders:KnownNetworks", "CIDR notation"),
            "known network in Staging" =>
                Single("Staging", "ForwardedHeaders:KnownNetworks:0", "172.30.80.0/24", "ForwardedHeaders:KnownNetworks", "must be empty outside Development and Testing"),
            "known network in Production" =>
                Single("Production", "ForwardedHeaders:KnownNetworks:0", "172.30.80.0/24", "ForwardedHeaders:KnownNetworks", "must be empty outside Development and Testing"),
            "demo seeding without a password" => new RefusedCase(
                "Testing",
                factory => factory.Settings["Seed:DemoData"] = "true",
                "Seed:DemoPassword",
                "12 or more characters when Seed:DemoData is true",
                _ => []),
            "demo seeding with an 11-character password" => SeedCase("Testing", Marker()[..11], "Seed:DemoPassword", "12 or more characters when Seed:DemoData is true"),
            "demo seeding in Production" => SeedCase("Production", $"{Marker()}-{Marker()}", "Seed:DemoData", "must be false in Production"),
            _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, "Unknown setting case."),
        };
    }

    private static RefusedCase KeyCase(string key, string kind)
    {
        var value = kind switch
        {
            "missing" => null,
            "blank" => "  ",
            "not base64" => $"{Marker()}!*",
            "of 31 bytes" => Convert.ToBase64String(RandomNumberGenerator.GetBytes(31)),
            "in base64url form" => UrlFormOfRandomKey(),
            "published 0x00" => Convert.ToBase64String(Run(0x00)),
            "published 0x20" => Convert.ToBase64String(Run(0x20)),
            "published 0x40" => Convert.ToBase64String(Run(0x40)),
            "published 0x00 in whsec_ form" => Whsec(Run(0x00)),
            "published 0x20 in whsec_ form" => Whsec(Run(0x20)),
            "published 0x40 in whsec_ form" => Whsec(Run(0x40)),
            "with all-equal bytes" => Convert.ToBase64String(Run(0x41, 32, step: 0)),
            "with a constant step" => Convert.ToBase64String(Run(0x07, 32, step: 3)),
            "with a descending step" => Convert.ToBase64String(Run(0xF0, 32, step: -1)),
            "of 64 bytes with a constant step" => Convert.ToBase64String(Run(0x90, 64, step: 1)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown key case."),
        };
        var rule = kind switch
        {
            "missing" or "blank" => "must be set",
            "published 0x00" or "published 0x20" or "published 0x40" => PublishedRule,
            "with all-equal bytes" or "with a constant step" or "with a descending step" or "of 64 bytes with a constant step" => PatternedRule,
            _ => KeyFormatRule,
        };
        return Single("Testing", key, value, key, rule);
    }

    private static RefusedCase Single(string environment, string setting, string? value, string key, string ruleFragment) => new(
        environment,
        factory => factory.Settings[setting] = value,
        key,
        ruleFragment,
        _ => value is { Length: >= 8 } && value.Trim().Length > 0 ? [value] : []);

    private static RefusedCase NoProxyCase(string environment) => new(
        environment,
        factory =>
        {
            factory.Settings.Remove(FirstKnownProxy);
            factory.DefaultKnownProxies = [];
        },
        KnownProxies,
        ProxyRequiredRule,
        _ => []);

    private static RefusedCase SeedCase(string environment, string password, string key, string ruleFragment) => new(
        environment,
        factory =>
        {
            factory.Settings["Seed:DemoData"] = "true";
            factory.Settings["Seed:DemoPassword"] = password;
        },
        key,
        ruleFragment,
        _ => [password]);

    private async Task<StartResult> StartAsync(string environment, Action<ApiFactory> configure)
    {
        var factory = Fixture.CreateFactory(factory =>
        {
            EndpointInventoryTests.ConfigureEnvironment(factory, environment);
            configure(factory);
        });
        Exception? failure = null;
        try
        {
            using var client = factory.CreateClient();
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        await factory.DisposeAsync();
        return new StartResult(factory, failure);
    }

    private static void AssertRefused(StartResult result, string key, string ruleFragment, IReadOnlyList<string> sentinels)
    {
        Assert.True(result.Failure is not null, $"The host started although {key} is invalid.");
        Assert.Contains(Flatten(result.Failure), exception =>
            exception is OptionsValidationException validation
            && validation.Failures.Any(message => message.StartsWith($"{key}: ", StringComparison.Ordinal)
                && message.Contains(ruleFragment, StringComparison.Ordinal)));
        var logged = Assert.Single(result.ConfigInvalid, logEvent => CapturingLogSink.Scalar(logEvent, "Key") == key);
        Assert.Equal(LogEventLevel.Fatal, logged.Level);
        Assert.Contains(ruleFragment, CapturingLogSink.Scalar(logged, "Rule"), StringComparison.Ordinal);
        Assert.All(result.ConfigInvalid, logEvent => Assert.All(
            logEvent.Properties.Keys,
            name => Assert.Contains(name, _allowedEventProperties)));
        AssertNoValue(result, sentinels);
    }

    private static void AssertNoValue(StartResult result, IReadOnlyList<string> sentinels)
    {
        var texts = Flatten(result.Failure)
            .Select(exception => $"exception {exception.GetType().Name}: {exception.Message}")
            .Concat(result.Events.Select(logEvent => $"log event {logEvent.MessageTemplate.Text}: {CapturingLogSink.Text(logEvent)}"))
            .ToList();
        var findings = sentinels
            .Where(sentinel => !string.IsNullOrWhiteSpace(sentinel))
            .SelectMany(sentinel => texts
                .Where(text => text.Contains(sentinel, StringComparison.Ordinal))
                .Select(text => $"the value under test appears in {text.Split(':')[0]}"))
            .ToList();
        Assert.True(findings.Count == 0, string.Join("\n", findings));
    }

    private static IEnumerable<Exception> Flatten(Exception? exception)
    {
        if (exception is null)
        {
            yield break;
        }

        yield return exception;
        var inner = exception is AggregateException aggregate
            ? aggregate.InnerExceptions
            : exception.InnerException is { } single ? [single] : [];
        foreach (var child in inner.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static string PasswordOf(string connectionString) => new NpgsqlConnectionStringBuilder(connectionString).Password!;

    private static byte[] Run(int first, int length = 32, int step = 1)
    {
        var bytes = new byte[length];
        for (var index = 0; index < length; index++)
        {
            bytes[index] = (byte)(first + (index * step));
        }

        return bytes;
    }

    private static string Whsec(byte[] bytes) => SecretKeyRules.WebhookSecretPrefix + Convert.ToBase64String(bytes);

    private static string UrlFormOfRandomKey()
    {
        while (true)
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            var standard = Convert.ToBase64String(bytes);
            if (standard.Contains('+', StringComparison.Ordinal) || standard.Contains('/', StringComparison.Ordinal))
            {
                return Base64Url.EncodeToString(bytes);
            }
        }
    }

    private static string Marker() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    private sealed record RefusedCase(
        string Environment,
        Action<ApiFactory> Apply,
        string Key,
        string RuleFragment,
        Func<ApiFactory, IReadOnlyList<string>> Sentinels);

    private sealed record StartResult(ApiFactory Factory, Exception? Failure)
    {
        public IReadOnlyList<LogEvent> Events { get; } = Factory.LogSink.Events;

        public IReadOnlyList<LogEvent> ConfigInvalid { get; } = Factory.LogSink.Named(ConfigInvalidEvent);
    }
}
