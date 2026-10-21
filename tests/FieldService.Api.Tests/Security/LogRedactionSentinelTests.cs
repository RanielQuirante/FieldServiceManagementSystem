using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Infrastructure;
using Serilog.Events;

namespace FieldService.Api.Tests.Security;

[Collection(DatabaseCollection.Name)]
public sealed class LogRedactionSentinelTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private static readonly string[] _clientIpEvents =
    [
        SecurityLog.SignInFailedEvent,
        SecurityLog.ReuseDetectedEvent,
        SecurityLog.RateLimitRejectedEvent,
    ];

    [Fact]
    public async Task SignInRefreshCreateAndSignOutLeaveNoSecretOrPersonalDatumInTheCapturedEventsOrTheLogFile()
    {
        var flow = await RunFlowAsync();

        var findings = flow.Secrets
            .SelectMany(secret => flow.Records
                .Where(record => record.Text.Contains(secret.Value, StringComparison.OrdinalIgnoreCase))
                .Select(record => $"{secret.Label} appears in {record.Location}"))
            .Distinct()
            .ToList();

        Assert.True(findings.Count == 0, "Log redaction failures:\n" + string.Join("\n", findings));
        Assert.True(flow.FileLines.Count > 20, $"The log file holds only {flow.FileLines.Count} lines.");
        Assert.All(
            new[]
            {
                SecurityLog.SignInSucceededEvent,
                SecurityLog.SignInFailedEvent,
                SecurityLog.ReuseDetectedEvent,
                SecurityLog.SignOutEvent,
                SecurityLog.RateLimitRejectedEvent,
                "seed.demo-data.completed",
            },
            name => Assert.Contains(flow.FileLines, line => line.Contains($"\"Name\":\"{name}\"", StringComparison.Ordinal)));
        Assert.Contains(flow.FileLines, line => line.Contains("\"RequestPath\":\"/api/jobs\"", StringComparison.Ordinal));
        Assert.True(flow.Secrets.Count >= 40, $"Only {flow.Secrets.Count} sentinel values were scanned for.");
    }

    [Fact]
    public async Task ClientIpAppearsOnlyOnTheFailedSignInReuseAndRateLimitEvents()
    {
        var flow = await RunFlowAsync();

        var withClientIp = flow.Events
            .Where(logEvent => !RateLimitTests.IsAbsentOrNull(logEvent, "ClientIp"))
            .ToList();
        var misplaced = flow.Events
            .Where(logEvent => !_clientIpEvents.Any(name => CapturingLogSink.HasEventName(logEvent, name)))
            .Where(logEvent => flow.Addresses.Any(address => CapturingLogSink.Text(logEvent).Contains(address, StringComparison.Ordinal)))
            .Select(logEvent => logEvent.MessageTemplate.Text)
            .ToList();
        var misplacedLines = flow.FileLines
            .Where(line => !_clientIpEvents.Any(name => line.Contains($"\"Name\":\"{name}\"", StringComparison.Ordinal)))
            .Count(line => flow.Addresses.Any(address => line.Contains(address, StringComparison.Ordinal)));

        Assert.NotEmpty(withClientIp);
        Assert.All(withClientIp, logEvent => Assert.Contains(_clientIpEvents, name => CapturingLogSink.HasEventName(logEvent, name)));
        Assert.Contains(withClientIp, logEvent => CapturingLogSink.HasEventName(logEvent, SecurityLog.ReuseDetectedEvent));
        Assert.Contains(withClientIp, logEvent => CapturingLogSink.HasEventName(logEvent, SecurityLog.RateLimitRejectedEvent));
        Assert.True(misplaced.Count == 0, "Client addresses in other events:\n" + string.Join("\n", misplaced));
        Assert.Equal(0, misplacedLines);
    }

    [Fact]
    public async Task AFailedSignInForAnUnknownAccountLogsTheReasonAndNoEmailOrUser()
    {
        var flow = await RunFlowAsync();

        var unknown = Assert.Single(
            flow.Events.Where(logEvent => CapturingLogSink.HasEventName(logEvent, SecurityLog.SignInFailedEvent)),
            logEvent => CapturingLogSink.Scalar(logEvent, "Reason") == SecurityLog.UnknownAccount);

        Assert.Equal(LogEventLevel.Warning, unknown.Level);
        Assert.True(RateLimitTests.IsAbsentOrNull(unknown, "UserId"), "The unknown-account event names a user.");
        Assert.DoesNotContain("@", CapturingLogSink.Text(unknown), StringComparison.Ordinal);
        var wrongPassword = Assert.Single(
            flow.Events.Where(logEvent => CapturingLogSink.HasEventName(logEvent, SecurityLog.SignInFailedEvent)),
            logEvent => CapturingLogSink.Scalar(logEvent, "Reason") == SecurityLog.WrongPassword);
        Assert.False(RateLimitTests.IsAbsentOrNull(wrongPassword, "UserId"), "The wrong-password event does not name the user id.");
    }

    private async Task<Flow> RunFlowAsync()
    {
        var marker = Marker();
        var password = $"Sentinel-{marker}";
        var wrongPassword = $"Wrong-{Marker()}";
        var unknownEmail = $"unknown-{marker}@fieldservice.example";
        var search = $"search-{marker}";
        var job = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["job title"] = $"Sentinel title {marker}",
            ["job description"] = $"Sentinel description {marker}",
            ["job customer"] = $"Sentinel Customer {marker}",
            ["job site address"] = $"{RandomNumberGenerator.GetInt32(100, 999)} Sentinel Street {marker}",
            ["job access notes"] = $"Gate code {marker}",
            ["job contact name"] = $"Sentinel Contact {marker}",
            ["job contact phone"] = $"(555) 019-{RandomNumberGenerator.GetInt32(10000):D4}",
            ["job equipment"] = $"Sentinel unit {marker}",
        };
        var secrets = new List<(string Label, string Value)>
        {
            ("the sentinel password", password),
            ("the wrong password", wrongPassword),
            ("the unknown email", unknownEmail),
            ("the search term", search),
        };
        secrets.AddRange(job.Select(entry => (entry.Key, entry.Value)));
        foreach (var user in DemoData.Users)
        {
            secrets.Add(($"seeded name {user.Person}", user.DisplayName));
            secrets.Add(($"seeded email {user.Person}", user.Email));
            if (user.PhoneNumber is { } phone)
            {
                secrets.Add(($"seeded phone {user.Person}", phone));
            }
        }

        foreach (var seeded in DemoData.Jobs)
        {
            secrets.Add(($"seeded customer of JOB-{seeded.Number}", seeded.CustomerName));
            secrets.Add(($"seeded site address of JOB-{seeded.Number}", seeded.SiteAddress));
            AddOptional(secrets, $"seeded access notes of JOB-{seeded.Number}", seeded.SiteAccessNotes);
            AddOptional(secrets, $"seeded contact name of JOB-{seeded.Number}", seeded.ContactName);
            AddOptional(secrets, $"seeded contact phone of JOB-{seeded.Number}", seeded.ContactPhone);
        }

        var dana = DemoData.Users.Single(user => user.Person == DemoPerson.DanaWhitfield);
        var marco = DemoData.Users.Single(user => user.Person == DemoPerson.MarcoReyes);
        var factory = Fixture.CreateFactory(factory =>
        {
            factory.EnvironmentName = "Development";
            factory.Settings["Logs:FileEnabled"] = "true";
            factory.Settings["Seed:DemoData"] = "true";
            factory.Settings["Seed:DemoPassword"] = password;
        });
        var addresses = new List<string>();
        try
        {
            using var web = factory.CreateApiClient();
            using var mobile = factory.CreateApiClient();
            using var stranger = factory.CreateApiClient();
            using var flood = factory.CreateApiClient();
            addresses.AddRange([web.RemoteAddress.ToString(), mobile.RemoteAddress.ToString(), stranger.RemoteAddress.ToString(), flood.RemoteAddress.ToString()]);

            using var signIn = await web.SignInAsync(dana.Email, password, SessionClient.Web);
            Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
            var firstSession = await ApiClient.ReadSessionAsync(signIn);
            var firstCookie = ApiClient.RefreshCookieOf(signIn)!.Value.ToString();
            using var refresh = await web.RefreshWithCookieAsync(firstCookie);
            Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
            var secondSession = await ApiClient.ReadSessionAsync(refresh);
            var secondCookie = ApiClient.RefreshCookieOf(refresh)!.Value.ToString();
            web.AccessToken = secondSession.AccessToken;

            using var created = await web.PostJsonAsync("/api/jobs", new
            {
                title = job["job title"],
                description = job["job description"],
                priority = "High",
                customerName = job["job customer"],
                siteAddress = job["job site address"],
                siteAccessNotes = job["job access notes"],
                contactName = job["job contact name"],
                contactPhone = job["job contact phone"],
                equipment = job["job equipment"],
                scheduledStart = UtcInstant.Truncate(factory.Clock.GetUtcNow()).AddDays(1),
                scheduledEnd = UtcInstant.Truncate(factory.Clock.GetUtcNow()).AddDays(1).AddHours(2),
                checklistTemplateId = ChecklistTemplateSeed.TemplateId(1),
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var listed = await web.GetAsync($"/api/jobs?search={Uri.EscapeDataString(search)}&pageSize=12");
            using var detail = await web.GetAsync(created.Headers.Location!.OriginalString);
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

            factory.Clock.Advance(TimeSpan.FromSeconds(31));
            using var reuse = await web.RefreshWithCookieAsync(firstCookie);
            Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
            using var signOut = await web.SignOutWithCookieAsync(secondCookie);
            Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);

            using var mobileSignIn = await mobile.SignInAsync(marco.Email, password, SessionClient.Mobile);
            Assert.Equal(HttpStatusCode.OK, mobileSignIn.StatusCode);
            var mobileSession = await ApiClient.ReadSessionAsync(mobileSignIn);
            using var mobileRefresh = await mobile.RefreshWithBodyAsync(mobileSession.RefreshToken);
            Assert.Equal(HttpStatusCode.OK, mobileRefresh.StatusCode);
            var mobileRefreshed = await ApiClient.ReadSessionAsync(mobileRefresh);
            using var mobileSignOut = await mobile.SignOutWithBodyAsync(mobileRefreshed.RefreshToken);
            Assert.Equal(HttpStatusCode.NoContent, mobileSignOut.StatusCode);

            using var unknown = await stranger.SignInAsync(unknownEmail, password, SessionClient.Web);
            using var wrong = await stranger.SignInAsync(dana.Email, wrongPassword, SessionClient.Web);
            using var wrongApplication = await stranger.SignInAsync(marco.Email, password, SessionClient.Web);
            Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, wrongApplication.StatusCode);

            for (var attempt = 1; attempt <= 11; attempt++)
            {
                using var limited = await flood.PostJsonAsync(ApiClient.SignInPath, new { });
            }

            secrets.AddRange(
            [
                ("the first access token", firstSession.AccessToken),
                ("the second access token", secondSession.AccessToken),
                ("the mobile access token", mobileSession.AccessToken),
                ("the refreshed mobile access token", mobileRefreshed.AccessToken),
                ("the first refresh cookie", firstCookie),
                ("the second refresh cookie", secondCookie),
                ("the mobile refresh token", mobileSession.RefreshToken!),
                ("the refreshed mobile refresh token", mobileRefreshed.RefreshToken!),
            ]);
        }
        finally
        {
            await factory.DisposeAsync();
        }

        var lines = ReadLogFiles(factory.LogsDirectory);
        var records = factory.LogSink.Events
            .Select(logEvent => new LogRecord($"captured event \"{logEvent.MessageTemplate.Text}\"", CapturingLogSink.Text(logEvent)))
            .Concat(lines.Select((line, index) => new LogRecord($"log file line {index + 1}", DecodedText(line))))
            .ToList();
        return new Flow(secrets, records, factory.LogSink.Events, lines, addresses);
    }

    private static List<string> ReadLogFiles(string directory)
    {
        var files = Directory.Exists(directory) ? Directory.GetFiles(directory) : [];
        var lines = files
            .Order(StringComparer.Ordinal)
            .SelectMany(file => File.ReadAllLines(file, Encoding.UTF8))
            .Where(line => line.Length > 0)
            .ToList();
        foreach (var file in files)
        {
            File.Delete(file);
        }

        if (Directory.Exists(directory))
        {
            Directory.Delete(directory);
        }

        return lines;
    }

    private static string DecodedText(string line)
    {
        var text = new StringBuilder(line).Append(' ');
        using var document = JsonDocument.Parse(line);
        Append(document.RootElement, text);
        return text.ToString();
    }

    private static void Append(JsonElement element, StringBuilder text)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var member in element.EnumerateObject())
                {
                    text.Append(member.Name).Append(' ');
                    Append(member.Value, text);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Append(item, text);
                }

                break;
            case JsonValueKind.String:
                text.Append(element.GetString()).Append(' ');
                break;
            default:
                text.Append(element.GetRawText()).Append(' ');
                break;
        }
    }

    private static void AddOptional(List<(string Label, string Value)> secrets, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            secrets.Add((label, value));
        }
    }

    private static string Marker() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6));

    private sealed record LogRecord(string Location, string Text);

    private sealed record Flow(
        List<(string Label, string Value)> Secrets,
        List<LogRecord> Records,
        IReadOnlyList<LogEvent> Events,
        List<string> FileLines,
        List<string> Addresses);
}
