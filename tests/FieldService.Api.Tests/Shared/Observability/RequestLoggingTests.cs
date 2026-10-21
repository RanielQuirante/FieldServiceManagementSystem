using System.Net;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Health;
using FieldService.Api.Shared.Observability;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Serilog.Events;

namespace FieldService.Api.Tests.Shared.Observability;

[Collection(DatabaseCollection.Name)]
public sealed class RequestLoggingTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string MinimumLevelKey = "Serilog:MinimumLevel:Default";

    private const string OverrideKey = "Serilog:MinimumLevel:Override:";

    [Fact]
    public async Task EachRequestWritesOneCompletionEventWithIdentifiersAndWithoutTheQueryString()
    {
        await using var factory = Fixture.CreateFactory(factory => factory.Settings[MinimumLevelKey] = "Information");
        var userId = Guid.CreateVersion7();
        var marker = $"query-{Guid.NewGuid():N}";
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.Issue(factory, userId, UserRole.Administrator).Value;

        using var response = await client.PostAsync($"{ApiClient.SignOutPath}?probe={marker}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var completion = Assert.Single(Completions(factory, ApiClient.SignOutPath));
        Assert.Equal(LogEventLevel.Information, completion.Level);
        Assert.Equal("POST", CapturingLogSink.Scalar(completion, "RequestMethod"));
        Assert.Equal("204", CapturingLogSink.Scalar(completion, "StatusCode"));
        Assert.True(completion.Properties.ContainsKey("Elapsed"));
        Assert.Equal(userId.ToString(), CapturingLogSink.Scalar(completion, "UserId"));
        Assert.Equal("Administrator", CapturingLogSink.Scalar(completion, "Role"));
        Assert.Equal(client.LastCorrelationId, CapturingLogSink.Scalar(completion, "CorrelationId"));
        Assert.False(completion.Properties.ContainsKey("ClientIp"));
        Assert.All(factory.LogSink.Events, logEvent =>
        {
            var text = CapturingLogSink.Text(logEvent);
            Assert.DoesNotContain(marker, text, StringComparison.Ordinal);
            Assert.DoesNotContain(client.AccessToken, text, StringComparison.Ordinal);
            Assert.DoesNotContain(client.RemoteAddress.ToString(), text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task AnAnonymousRequestIsLoggedWithoutAUserAndHealthRequestsAreLoggedAtVerbose()
    {
        await using var factory = Fixture.CreateFactory(factory => factory.Settings[MinimumLevelKey] = "Verbose");
        using var client = factory.CreateApiClient();

        using var anonymous = await client.PostAsync(ApiClient.SignOutPath);
        using var health = await client.GetAsync(HealthRegistration.LivePath);

        var apiCompletion = Assert.Single(Completions(factory, ApiClient.SignOutPath));
        Assert.Equal(LogEventLevel.Information, apiCompletion.Level);
        Assert.False(apiCompletion.Properties.ContainsKey("UserId"));
        Assert.False(apiCompletion.Properties.ContainsKey("Role"));
        var healthCompletion = Assert.Single(Completions(factory, HealthRegistration.LivePath));
        Assert.Equal(LogEventLevel.Verbose, healthCompletion.Level);
        Assert.Equal("200", CapturingLogSink.Scalar(healthCompletion, "StatusCode"));
    }

    [Fact]
    public async Task WithTheMinimumLevelAtWarningNoInformationCompletionEventIsWritten()
    {
        await using var factory = Fixture.CreateFactory(factory => factory.Settings[MinimumLevelKey] = "Warning");
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsync(ApiClient.SignOutPath);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(Completions(factory, ApiClient.SignOutPath));
        Assert.DoesNotContain(factory.LogSink.Events, logEvent => logEvent.Level < LogEventLevel.Warning);
    }

    [Fact]
    public async Task AnInvalidInboundCorrelationIdIsReplacedInTheHeaderAndTheLogByTheTraceId()
    {
        await using var factory = Fixture.CreateFactory(factory => factory.Settings[MinimumLevelKey] = "Information");
        using var http = factory.CreateClient();
        using var valid = new HttpRequestMessage(HttpMethod.Post, ApiClient.SignOutPath);
        valid.Headers.Add(CorrelationId.HeaderName, "client-chosen.id_1");
        using var invalid = new HttpRequestMessage(HttpMethod.Post, ApiClient.SignOutPath);
        invalid.Headers.TryAddWithoutValidation(CorrelationId.HeaderName, "not valid!");

        using var echoed = await http.SendAsync(valid, CancellationToken);
        using var replaced = await http.SendAsync(invalid, CancellationToken);

        Assert.Equal("client-chosen.id_1", Assert.Single(echoed.Headers.GetValues(CorrelationId.HeaderName)));
        var traceId = Assert.Single(replaced.Headers.GetValues(CorrelationId.HeaderName));
        Assert.Matches("^[0-9a-f]{32}$", traceId);
        var logged = Completions(factory, ApiClient.SignOutPath).Select(logEvent => CapturingLogSink.Scalar(logEvent, "CorrelationId"));
        Assert.Equal(["client-chosen.id_1", traceId], logged);
    }

    [Theory]
    [InlineData("Production", "Information", "Warning", "Warning", "Warning", "Warning")]
    [InlineData("Staging", "Information", "Warning", "Warning", "Warning", "Warning")]
    [InlineData("Development", "Debug", "Information", "Warning", "Information", "Information")]
    [InlineData("Testing", "Warning", "Warning", "Warning", "Warning", "Warning")]
    public void TheSettingsFilesGiveEachEnvironmentItsLevels(
        string environment,
        string defaultLevel,
        string aspNetCore,
        string hostingDiagnostics,
        string entityFramework,
        string httpClient)
    {
        var directory = Path.Combine(RepositoryPaths.Root, "src", "FieldService.Api");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(directory, "appsettings.json"), optional: false)
            .AddJsonFile(Path.Combine(directory, $"appsettings.{environment}.json"), optional: true)
            .Build();

        Assert.Equal(defaultLevel, configuration[MinimumLevelKey]);
        Assert.Equal(aspNetCore, configuration[$"{OverrideKey}Microsoft.AspNetCore"]);
        Assert.Equal(hostingDiagnostics, configuration[$"{OverrideKey}Microsoft.AspNetCore.Hosting.Diagnostics"]);
        Assert.Equal(entityFramework, configuration[$"{OverrideKey}Microsoft.EntityFrameworkCore"]);
        Assert.Equal(httpClient, configuration[$"{OverrideKey}System.Net.Http"]);
        Assert.Equal(environment is "Production" or "Staging", !File.Exists(Path.Combine(directory, $"appsettings.{environment}.json")));
    }

    [Fact]
    public async Task InDevelopmentASearchTermInTheQueryNeverReachesTheLogWhileAspNetCoreInformationEventsDo()
    {
        await using var factory = Fixture.CreateFactory(factory =>
        {
            factory.EnvironmentName = "Development";
            factory.Settings["Logs:FileEnabled"] = "false";
        });
        var sentinel = $"sentinel-{Guid.NewGuid():N}";
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, UserRole.Administrator);

        using var response = await client.GetAsync($"/api/jobs?search={sentinel}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(factory.LogSink.Events, logEvent =>
            logEvent.Level == LogEventLevel.Information
            && CapturingLogSink.Scalar(logEvent, "SourceContext") is { } source
            && source.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.Contains(factory.LogSink.Events, logEvent =>
            logEvent.Level == LogEventLevel.Information
            && CapturingLogSink.Scalar(logEvent, "SourceContext") == "Microsoft.EntityFrameworkCore.Database.Command"
            && CapturingLogSink.Text(logEvent).Contains("ILIKE", StringComparison.Ordinal));
        Assert.All(factory.LogSink.Events, logEvent =>
            Assert.DoesNotContain(sentinel, CapturingLogSink.Text(logEvent), StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADevelopmentHostWithTheFileSinkTurnedOffCreatesNoLogsDirectory()
    {
        await using var factory = Fixture.CreateFactory(factory =>
        {
            factory.EnvironmentName = "Development";
            factory.Settings["Logs:FileEnabled"] = "false";
        });
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(HealthRegistration.LivePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(Directory.Exists(factory.LogsDirectory), "The Development host created its logs directory.");
    }

    private static List<LogEvent> Completions(ApiFactory factory, string path) =>
    [
        .. factory.LogSink.Events.Where(logEvent =>
            logEvent.Properties.ContainsKey("StatusCode")
            && logEvent.Properties.ContainsKey("Elapsed")
            && CapturingLogSink.Scalar(logEvent, "RequestPath") == path),
    ];
}
