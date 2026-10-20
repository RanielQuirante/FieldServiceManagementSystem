using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using FieldService.Api.Shared.Observability;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FieldService.Api.Tests.Security;

[Collection(DatabaseCollection.Name)]
public sealed partial class FileLoggingTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string ProbePath = "/api/file-logging-probe";

    private const string DayFormat = "yyyyMMdd";

    [Fact]
    public async Task OneRequestWritesAClefRequestEventToTheDailyFileInTheLogsDirectory()
    {
        var queryMarker = $"query-{Guid.NewGuid():N}";
        var factory = Fixture.CreateFactory(factory =>
        {
            factory.Settings["Logs:FileEnabled"] = "true";
            factory.Settings["Serilog:MinimumLevel:Default"] = "Information";
        });
        var dayBefore = LocalDay();
        string correlationId;
        HttpStatusCode status;
        try
        {
            using var client = factory.CreateApiClient();
            using var response = await client.GetAsync($"{ProbePath}?probe={queryMarker}");
            correlationId = client.LastCorrelationId!;
            status = response.StatusCode;
        }
        finally
        {
            await factory.DisposeAsync();
        }

        var dayAfter = LocalDay();
        var files = Directory.Exists(factory.LogsDirectory) ? Directory.GetFiles(factory.LogsDirectory) : [];
        try
        {
            Assert.Equal(HttpStatusCode.NotFound, status);
            var file = Assert.Single(files);
            Assert.Contains(Path.GetFileName(file), new[] { $"api-{dayBefore}.log", $"api-{dayAfter}.log" });
            var content = await File.ReadAllTextAsync(file, CancellationToken);
            var events = content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(ParseLine).ToList();
            Assert.All(events, logEvent =>
            {
                Assert.True(logEvent.TryGetProperty("@t", out _), "A CLEF line has no @t.");
                Assert.True(logEvent.TryGetProperty("@mt", out _), "A CLEF line has no @mt.");
            });
            var request = Assert.Single(events, logEvent =>
                logEvent.TryGetProperty("RequestPath", out var path)
                && path.GetString() == ProbePath
                && logEvent.TryGetProperty("StatusCode", out _));
            Assert.Equal("GET", request.GetProperty("RequestMethod").GetString());
            Assert.Equal(404, request.GetProperty("StatusCode").GetInt32());
            Assert.True(request.GetProperty("Elapsed").GetDouble() >= 0, "Elapsed is negative.");
            Assert.Equal(correlationId, request.GetProperty("CorrelationId").GetString());
            Assert.Matches(TraceIdPattern(), request.GetProperty("@tr").GetString());
            Assert.DoesNotContain(queryMarker, content, StringComparison.Ordinal);
        }
        finally
        {
            foreach (var file in files)
            {
                File.Delete(file);
            }

            if (Directory.Exists(factory.LogsDirectory))
            {
                Directory.Delete(factory.LogsDirectory);
            }
        }
    }

    [Fact]
    public void ConfiguredRetentionKeepsFourteenFilesOfAtMostFiftyMegabytes()
    {
        var logs = Fixture.Factory.Services.GetRequiredService<IOptions<LogsOptions>>().Value;

        Assert.Equal(14, logs.RetainedFileCountLimit);
        Assert.Equal(50L * 1024 * 1024, logs.FileSizeLimitBytes);
    }

    private static JsonElement ParseLine(string line)
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.Clone();
    }

    private static string LocalDay()
    {
        return DateTime.Now.ToString(DayFormat, CultureInfo.InvariantCulture);
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex TraceIdPattern();
}
