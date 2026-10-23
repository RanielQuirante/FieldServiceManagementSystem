using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Shared.Realtime;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    public async Task SignInRefreshCreateStatusChangesAndSignOutLeaveNoSecretOrPersonalDatumInTheCapturedEventsOrTheLogFile()
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
                SecurityLog.SocketTicketIssuedEvent,
                RealtimeLog.DispatchedEvent,
                "seed.demo-data.completed",
            },
            name => Assert.Contains(flow.FileLines, line => line.Contains($"\"Name\":\"{name}\"", StringComparison.Ordinal)));
        Assert.Equal(
            flow.Webhooks,
            flow.FileLines.Count(line => line.Contains($"\"Name\":\"{RealtimeLog.DispatchedEvent}\"", StringComparison.Ordinal)));
        Assert.Contains(flow.FileLines, line => line.Contains("\"RequestPath\":\"/api/auth/socket-tickets\"", StringComparison.Ordinal));
        Assert.Contains(flow.FileLines, line => line.Contains("\"RequestPath\":\"/api/jobs\"", StringComparison.Ordinal));
        Assert.True(
            flow.FileLines.Count(line => line.Contains($"\"RequestPath\":\"{flow.StatusPath}\"", StringComparison.Ordinal)) >= 3,
            "The log file holds fewer than three lines for the status changes.");
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
        var holdNote = $"Sentinel hold note {marker}";
        var statusPath = string.Empty;
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
            ("the hold note", holdNote),
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
        var notifications = new RecordingNotifications();
        var factory = Fixture.CreateFactory(factory =>
        {
            factory.EnvironmentName = "Development";
            factory.Settings["Logs:FileEnabled"] = "true";
            factory.Settings["Seed:DemoData"] = "true";
            factory.Settings["Seed:DemoPassword"] = password;
            factory.Settings[NotificationsOptions.BaseUrlName] = RecordingNotifications.BaseUrl;
            factory.TestServices = services =>
            {
                services.RemoveAll<IJobEventQueue>();
                services.AddSingleton<IJobEventQueue>(provider => provider.GetRequiredService<JobEventQueue>());
                services.AddHttpClient<NotificationsClient>().ConfigurePrimaryHttpMessageHandler(() => notifications);
            };
        });
        foreach (var name in new[] { AuthOptions.AccessTokenKeyName, AuthOptions.SocketTicketKeyName, NotificationsOptions.WebhookSecretName })
        {
            var value = factory.Settings[name]!;
            secrets.Add(($"the configured {name}", value));
            secrets.Add(($"the key part of {name}", value[(value.IndexOf('_', StringComparison.Ordinal) + 1)..]));
        }

        var addresses = new List<string>();
        var webhooks = 0;
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
            using var ticketResponse = await web.PostAsync(SocketTicketClaimsTests.SocketTicketsPath);
            Assert.Equal(HttpStatusCode.OK, ticketResponse.StatusCode);
            using var ticketBody = JsonDocument.Parse(await ticketResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var ticket = ticketBody.RootElement.GetProperty("ticket").GetString()!;
            secrets.Add(("the socket ticket", ticket));
            secrets.Add(("the socket ticket signature", ticket[(ticket.LastIndexOf('.') + 1)..]));

            await notifications.WaitForAsync(1);
            await WaitForLogsAsync(factory.LogSink, RealtimeLog.DispatchedEvent, 1);
            Assert.Empty(factory.LogSink.Named(RealtimeLog.DispatchFailedEvent));
            factory.Clock.Advance(TimeSpan.FromSeconds(31));
            using var reuse = await web.RefreshWithCookieAsync(firstCookie);
            Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
            using var signOut = await web.SignOutWithCookieAsync(secondCookie);
            Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
            using var ticketAfterSignOut = await web.PostAsync(SocketTicketClaimsTests.SocketTicketsPath);
            Assert.Equal(HttpStatusCode.Unauthorized, ticketAfterSignOut.StatusCode);

            using var mobileSignIn = await mobile.SignInAsync(marco.Email, password, SessionClient.Mobile);
            Assert.Equal(HttpStatusCode.OK, mobileSignIn.StatusCode);
            var mobileSession = await ApiClient.ReadSessionAsync(mobileSignIn);
            using var mobileRefresh = await mobile.RefreshWithBodyAsync(mobileSession.RefreshToken);
            Assert.Equal(HttpStatusCode.OK, mobileRefresh.StatusCode);
            var mobileRefreshed = await ApiClient.ReadSessionAsync(mobileRefresh);
            mobile.AccessToken = mobileRefreshed.AccessToken;
            statusPath = await StartHoldAndResumeAsync(mobile, holdNote);
            mobile.AccessToken = null;
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

            await notifications.WaitForAsync(4);
            await WaitForLogsAsync(factory.LogSink, RealtimeLog.DispatchedEvent, 4);
            Assert.Empty(factory.LogSink.Named(RealtimeLog.DispatchFailedEvent));
            foreach (var webhook in notifications.Requests)
            {
                secrets.Add(("a webhook signature", webhook.Signature));
                secrets.Add(("the base64 part of a webhook signature", webhook.Signature[(webhook.Signature.IndexOf(',', StringComparison.Ordinal) + 1)..]));
                secrets.Add(("a webhook body", webhook.Body));
            }

            webhooks = notifications.Requests.Count;
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
        return new Flow(secrets, records, factory.LogSink.Events, lines, addresses, statusPath, webhooks);
    }

    private static async Task WaitForLogsAsync(CapturingLogSink sink, string eventName, int count)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (sink.Named(eventName).Count < count)
        {
            Assert.True(DateTime.UtcNow < deadline, $"Only {sink.Named(eventName).Count} of {count} {eventName} entries were logged.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    private static async Task<string> StartHoldAndResumeAsync(ApiClient technician, string holdNote)
    {
        using var assigned = await technician.GetAsync("/api/technicians/me/jobs?view=active");
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        using var list = JsonDocument.Parse(await assigned.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var job = list.RootElement.GetProperty("items").EnumerateArray()
            .First(item => item.GetProperty("status").GetString() == "Assigned");
        var path = $"/api/jobs/{job.GetProperty("id").GetGuid()}/status";
        var version = job.GetProperty("version").GetUInt32();
        foreach (var (status, note) in new[] { ("InProgress", (string?)null), ("OnHold", holdNote), ("InProgress", null) })
        {
            using var changed = await technician.PutJsonAsync(path, new { status, note, version });
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            using var body = JsonDocument.Parse(await changed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal(status, body.RootElement.GetProperty("status").GetString());
            version = body.RootElement.GetProperty("version").GetUInt32();
        }

        return path;
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
        List<string> Addresses,
        string StatusPath,
        int Webhooks);

    private sealed record Webhook(string Signature, string Body)
    {
        public override string ToString() => "a recorded webhook";
    }

    private sealed class RecordingNotifications : HttpMessageHandler
    {
        public const string BaseUrl = "http://notifications.test:5090";

        private readonly ConcurrentQueue<Webhook> _requests = new();

        public IReadOnlyList<Webhook> Requests => [.. _requests];

        public async Task WaitForAsync(int count)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (_requests.Count < count)
            {
                Assert.True(DateTime.UtcNow < deadline, $"The notification service stub received {_requests.Count} of {count} events.");
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var signature = request.Headers.TryGetValues("webhook-signature", out var values) ? string.Join(' ', values) : string.Empty;
            _requests.Enqueue(new Webhook(signature, body));
            return new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent("{\"outcome\":\"broadcast\"}", Encoding.UTF8, "application/json"),
                RequestMessage = request,
            };
        }
    }
}
