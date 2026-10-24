using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldService.Api.Domain;
using FieldService.Api.Features.Diagnostics;
using FieldService.Api.Features.Diagnostics.ReportClientError;
using FieldService.Api.Shared.Errors;
using FieldService.Api.Shared.Http;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Infrastructure;
using FieldService.Api.Tests.Security;
using Serilog.Events;
using Serilog.Parsing;

namespace FieldService.Api.Tests.Features.Diagnostics;

[Collection(DatabaseCollection.Name)]
public sealed class ReportClientErrorTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string ClientErrorsPath = "/api/client-errors";

    private const string LengthMessageFormat = "Enter at most {0} characters.";

    private const string InvalidValueMessage = "The value is not valid.";

    private const string ReportedCorrelationId = "927ba8e3-24aa-4d0e-8ef1-d8c4203c37fe";

    private const string TestServerBodyLimitEvent = "RequestSizeFeatureNotFound";

    private static readonly string[] _eventProperties =
    [
        "Source", "UserId", "CorrelationId", "ErrorSource", "Route", "OccurredAt", "AppVersion", "ReportedCorrelationId", "ErrorMessage", "ErrorStack",
    ];

    private DateTimeOffset Now => UtcInstant.Truncate(Fixture.Factory.Clock.GetUtcNow());

    public static TheoryData<string, string, string> InvalidMembers => new()
    {
        { "message", "null", InvalidValueMessage },
        { "message", Text(""), "Enter the error message." },
        { "message", Text("   "), "Enter the error message." },
        { "message", Text(new string('m', 501)), LengthMessage(500) },
        { "message", Text("Broken\u0000value"), MultiLineAttribute.Message },
        { "message", "42", InvalidValueMessage },
        { "source", "null", InvalidValueMessage },
        { "source", Text(""), "Enter the error source." },
        { "source", Text(new string('s', 201)), LengthMessage(200) },
        { "source", Text("app.errorHandler\nsecond line"), SingleLineAttribute.Message },
        { "source", Text("app.errorHandler\t"), SingleLineAttribute.Message },
        { "route", Text("/" + new string('r', 200)), LengthMessage(200) },
        { "route", Text("/jobs?status=Assigned"), ReportClientErrorRequest.RouteMessage },
        { "route", Text("/jobs#top"), ReportClientErrorRequest.RouteMessage },
        { "route", Text("jobs"), ReportClientErrorRequest.RouteMessage },
        { "route", Text("/jobs list"), ReportClientErrorRequest.RouteMessage },
        { "route", Text("https://fieldservice.example/jobs"), ReportClientErrorRequest.RouteMessage },
        { "route", Text("/jobs\n"), ReportClientErrorRequest.RouteMessage },
        { "route", Text("/jobs/é"), ReportClientErrorRequest.RouteMessage },
        { "correlationId", Text(new string('c', 65)), ReportClientErrorRequest.CorrelationIdMessage },
        { "correlationId", Text("has space"), ReportClientErrorRequest.CorrelationIdMessage },
        { "correlationId", Text("a/b"), ReportClientErrorRequest.CorrelationIdMessage },
        { "stack", Text(new string('k', 2001)), LengthMessage(2000) },
        { "stack", Text("at A\u0007"), MultiLineAttribute.Message },
        { "occurredAt", "null", "Send the time the error occurred." },
        { "occurredAt", Text("2026-10-19T14:43:10.214"), InvalidValueMessage },
        { "occurredAt", Text("yesterday"), InvalidValueMessage },
        { "occurredAt", "1792420990", InvalidValueMessage },
        { "appVersion", Text(new string('1', 33)), ReportClientErrorRequest.AppVersionMessage },
        { "appVersion", Text("1.0 beta"), ReportClientErrorRequest.AppVersionMessage },
        { "appVersion", Text("1.0_beta"), ReportClientErrorRequest.AppVersionMessage },
    };

    [Fact]
    public async Task AValidReportReturns202WithNoBodyAndLogsOneWarningWithTheFixedProperties()
    {
        var administrator = await AdministratorAsync();
        using var client = ClientFor(administrator);
        var body = ValidBody();
        body["correlationId"] = ReportedCorrelationId;

        using var response = await client.PostAsync(ClientErrorsPath, Json(body.ToJsonString()));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(CancellationToken));
        Assert.Null(response.Content.Headers.ContentType);
        Assert.Null(response.Headers.Location);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(client.LastCorrelationId, Assert.Single(response.Headers.GetValues("X-Correlation-ID")));
        var logged = Assert.Single(
            Fixture.Factory.LogSink.Events,
            logEvent => logEvent.Level >= LogEventLevel.Warning && !CapturingLogSink.HasEventName(logEvent, TestServerBodyLimitEvent));
        Assert.True(CapturingLogSink.HasEventName(logged, SecurityLog.ClientErrorReportedEvent));
        Assert.Equal(LogEventLevel.Warning, logged.Level);
        Assert.Equal("4010", EventIdOf(logged));
        Assert.Equal(_eventProperties, TemplatePropertiesOf(logged));
        Assert.Equal("web", ValueOf(logged, "Source"));
        Assert.Equal(administrator.Id, ValueOf(logged, "UserId"));
        Assert.Equal(client.LastCorrelationId, ValueOf(logged, "CorrelationId"));
        Assert.Equal(ReportedCorrelationId, ValueOf(logged, "ReportedCorrelationId"));
        Assert.Equal("app.errorHandler", ValueOf(logged, "ErrorSource"));
        Assert.Equal("/jobs/01a15488-9680-7989-92af-ed07fc34a890", ValueOf(logged, "Route"));
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 14, 43, 10, 214, TimeSpan.Zero), ValueOf(logged, "OccurredAt"));
        Assert.Equal("1.0.0", ValueOf(logged, "AppVersion"));
        Assert.Equal("Cannot read properties of undefined (reading 'number')", ValueOf(logged, "ErrorMessage"));
        Assert.Equal("TypeError: Cannot read properties of undefined (reading 'number')\n    at JobHeader.vue:42:18", ValueOf(logged, "ErrorStack"));
    }

    [Theory]
    [MemberData(nameof(ClientErrorScrubberTests.Kinds), MemberType = typeof(ClientErrorScrubberTests))]
    public async Task EveryScrubberPatternInTheMessageSourceAndStackIsRedactedBeforeLogging(string kind)
    {
        using var client = ClientFor(await AdministratorAsync());
        var sentinel = ClientErrorScrubberTests.Sentinel.For(kind);
        var body = ValidBody();
        body["message"] = $"Request failed: {sentinel.Text}";
        body["source"] = $"app.errorHandler {sentinel.Text}";
        body["stack"] = $"Error\n    at fetchJob ({sentinel.Text})";

        using var response = await client.PostAsync(ClientErrorsPath, Json(body.ToJsonString()));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(CancellationToken));
        var logged = Assert.Single(Fixture.Factory.LogSink.Named(SecurityLog.ClientErrorReportedEvent));
        Assert.Equal($"Request failed: {sentinel.Replacement}", ValueOf(logged, "ErrorMessage"));
        Assert.Equal($"app.errorHandler {sentinel.Replacement}", ValueOf(logged, "ErrorSource"));
        Assert.Equal($"Error\n    at fetchJob ({sentinel.Replacement})", ValueOf(logged, "ErrorStack"));
        Assert.All(Fixture.Factory.LogSink.Events, logEvent =>
            Assert.DoesNotContain(sentinel.Secret, CapturingLogSink.Text(logEvent), StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARouteCorrelationIdOrAppVersionShapedLikeASecretIsRedactedToo()
    {
        using var client = ClientFor(await AdministratorAsync());
        var refresh = ClientErrorScrubberTests.Sentinel.RefreshShaped();
        var secret = $"whsec_{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(20))}";
        var jwtShaped = $"eyJ{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3))}.{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3))}.1";
        var body = ValidBody();
        body["route"] = $"/reset/{refresh}";
        body["correlationId"] = secret;
        body["appVersion"] = jwtShaped;

        using var response = await client.PostAsync(ClientErrorsPath, Json(body.ToJsonString()));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var logged = Assert.Single(Fixture.Factory.LogSink.Named(SecurityLog.ClientErrorReportedEvent));
        Assert.Equal("/reset/[redacted]", ValueOf(logged, "Route"));
        Assert.Equal("[redacted]", ValueOf(logged, "ReportedCorrelationId"));
        Assert.Equal("[redacted]", ValueOf(logged, "AppVersion"));
        foreach (var value in new[] { refresh, secret, jwtShaped })
        {
            Assert.All(Fixture.Factory.LogSink.Events, logEvent =>
                Assert.DoesNotContain(value, CapturingLogSink.Text(logEvent), StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task NoScrubberPatternAndNoUnknownMemberReachesAnyLogEventAtAnyLevel()
    {
        await using var factory = Fixture.CreateFactory(VerboseLogging);
        var administrator = await new TestData(Fixture, Now).CreateAdministratorAsync(CancellationToken);
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, administrator);
        var sentinels = ClientErrorScrubberTests.AllKinds.Select(ClientErrorScrubberTests.Sentinel.For).ToList();
        var unknown = Enumerable.Range(0, 4).Select(_ => $"unknown{Guid.NewGuid():N}").ToList();
        var body = ValidBody();
        body["message"] = $"Request failed: {string.Join(' ', sentinels.Select(sentinel => sentinel.Text))}";
        body["source"] = $"app.errorHandler {sentinels[0].Text}";
        body["stack"] = string.Join('\n', sentinels.Select(sentinel => $"    at call ({sentinel.Text})"));
        body["password"] = unknown[0];
        body["token"] = unknown[1];
        body["extra"] = new JsonObject { ["nested"] = unknown[2] };
        body["headers"] = new JsonArray(unknown[3]);
        factory.LogSink.Clear();

        using var response = await client.PostAsync(ClientErrorsPath, Json(body.ToJsonString()));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(CancellationToken));
        var events = factory.LogSink.Events;
        Assert.Contains(events, logEvent => logEvent.Level <= LogEventLevel.Debug);
        var logged = Assert.Single(factory.LogSink.Named(SecurityLog.ClientErrorReportedEvent));
        Assert.Equal(_eventProperties, TemplatePropertiesOf(logged));
        Assert.DoesNotContain(logged.Properties.Keys, name => name is "password" or "token" or "extra" or "headers");
        foreach (var value in sentinels.Select(sentinel => sentinel.Secret).Concat(unknown))
        {
            Assert.All(events, logEvent => Assert.DoesNotContain(value, CapturingLogSink.Text(logEvent), StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task EmptyOrMissingOptionalStringsAreLoggedAsAbsentAndEveryStringIsTrimmed()
    {
        using var client = ClientFor(await AdministratorAsync());
        var empty = ValidBody();
        empty["message"] = "  Boom  \n";
        empty["source"] = " router.onError ";
        empty["route"] = string.Empty;
        empty["correlationId"] = string.Empty;
        empty["appVersion"] = string.Empty;
        empty["stack"] = " \n\t ";
        var missing = ValidBody();
        missing.Remove("route");
        missing.Remove("correlationId");
        missing.Remove("appVersion");
        missing["stack"] = "\n    at handle (main.ts:9:3)\n";

        using var withEmpty = await client.PostAsync(ClientErrorsPath, Json(empty.ToJsonString()));
        using var withMissing = await client.PostAsync(ClientErrorsPath, Json(missing.ToJsonString()));

        Assert.Equal(HttpStatusCode.Accepted, withEmpty.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, withMissing.StatusCode);
        var logged = Fixture.Factory.LogSink.Named(SecurityLog.ClientErrorReportedEvent);
        Assert.Equal(2, logged.Count);
        Assert.Equal(("Boom", "router.onError"), (ValueOf(logged[0], "ErrorMessage"), ValueOf(logged[0], "ErrorSource")));
        foreach (var property in new[] { "Route", "ReportedCorrelationId", "AppVersion", "ErrorStack" })
        {
            Assert.Null(ValueOf(logged[0], property));
        }

        foreach (var property in new[] { "Route", "ReportedCorrelationId", "AppVersion" })
        {
            Assert.Null(ValueOf(logged[1], property));
        }

        Assert.Equal("at handle (main.ts:9:3)", ValueOf(logged[1], "ErrorStack"));
    }

    [Fact]
    public async Task AnOccurredAtWithAnOffsetIsLoggedAsTheUtcInstantInMilliseconds()
    {
        using var client = ClientFor(await AdministratorAsync());
        var body = ValidBody();
        body["occurredAt"] = "2026-10-19T10:43:10.2149-04:00";

        using var response = await client.PostAsync(ClientErrorsPath, Json(body.ToJsonString()));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var occurredAt = Assert.IsType<DateTimeOffset>(ValueOf(Assert.Single(Fixture.Factory.LogSink.Named(SecurityLog.ClientErrorReportedEvent)), "OccurredAt"));
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 14, 43, 10, 214, TimeSpan.Zero), occurredAt);
        Assert.Equal(TimeSpan.Zero, occurredAt.Offset);
    }

    [Theory]
    [MemberData(nameof(InvalidMembers))]
    public async Task AMemberThatBreaksTheFixedSchemaIsAFieldErrorKeyedByTheMemberAndNothingIsLogged(string member, string value, string message)
    {
        using var client = ClientFor(await AdministratorAsync());
        var body = ValidBody();
        body[member] = JsonNode.Parse(value);

        using var response = await client.PostAsync(ClientErrorsPath, Json(body.ToJsonString()));

        await ApiClient.AssertFieldErrorsAsync(response, member);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(message, Assert.Single(problem.RootElement.GetProperty("errors").GetProperty(member).EnumerateArray()).GetString());
        Assert.Empty(Fixture.Factory.LogSink.Named(SecurityLog.ClientErrorReportedEvent));
    }

    [Theory]
    [InlineData("message")]
    [InlineData("source")]
    [InlineData("occurredAt")]
    public async Task AMissingRequiredMemberIsAFieldErrorKeyedByTheMember(string member)
    {
        using var client = ClientFor(await AdministratorAsync());
        var body = ValidBody();
        body.Remove(member);

        using var response = await client.PostAsync(ClientErrorsPath, Json(body.ToJsonString()));

        await ApiClient.AssertFieldErrorsAsync(response, member);
    }

    [Fact]
    public async Task EveryBrokenMemberIsReportedTogetherAndNoValueIsEchoed()
    {
        using var client = ClientFor(await AdministratorAsync());
        var marker = $"marker{Guid.NewGuid():N}";
        var body = new JsonObject
        {
            ["message"] = marker + new string('m', 500),
            ["source"] = "",
            ["route"] = $"/jobs?search={marker}",
            ["correlationId"] = marker + " x",
            ["stack"] = marker + new string('k', 2000),
            ["occurredAt"] = null,
            ["appVersion"] = marker + "!",
        };

        using var response = await client.PostAsync(ClientErrorsPath, Json(body.ToJsonString()));

        var text = await response.Content.ReadAsStringAsync(CancellationToken);
        await ApiClient.AssertFieldErrorsAsync(response, "message", "source", "route", "correlationId", "stack", "occurredAt", "appVersion");
        Assert.DoesNotContain(marker, text, StringComparison.Ordinal);
        Assert.DoesNotContain("search", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAnonymousCallerGets401AndNothingIsLogged()
    {
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.PostAsync(ClientErrorsPath, Json(ValidBody().ToJsonString()));

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        Assert.Empty(Fixture.Factory.LogSink.Named(SecurityLog.ClientErrorReportedEvent));
    }

    [Fact]
    public async Task ATechnicianGets403EvenWithAnInvalidBodyAndNothingIsLogged()
    {
        var technician = await new TestData(Fixture, Now).CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        using var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TestTokens.For(Fixture.Factory, technician.User);

        using var valid = await client.PostAsync(ClientErrorsPath, Json(ValidBody().ToJsonString()));
        using var invalid = await client.PostAsync(ClientErrorsPath, Json("{}"));

        await ApiClient.AssertProblemAsync(valid, HttpStatusCode.Forbidden, "auth.forbidden");
        await ApiClient.AssertProblemAsync(invalid, HttpStatusCode.Forbidden, "auth.forbidden");
        Assert.Empty(Fixture.Factory.LogSink.Named(SecurityLog.ClientErrorReportedEvent));
    }

    [Fact]
    public async Task TheEleventhReportWithinAMinuteFromOneAdministratorGets429AndAnotherAdministratorIsNotAffected()
    {
        using var client = ClientFor(await AdministratorAsync());
        using var other = ClientFor(await AdministratorAsync());
        var body = ValidBody().ToJsonString();
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 1; attempt <= RateLimitPolicies.ClientErrorLimit; attempt++)
        {
            using var response = await client.PostAsync(ClientErrorsPath, Json(body));
            statuses.Add(response.StatusCode);
        }

        using var limited = await client.PostAsync(ClientErrorsPath, Json(body));
        using var unaffected = await other.PostAsync(ClientErrorsPath, Json(body));

        Assert.Equal(10, RateLimitPolicies.ClientErrorLimit);
        Assert.Equal(Enumerable.Repeat(HttpStatusCode.Accepted, RateLimitPolicies.ClientErrorLimit), statuses);
        await RateLimitTests.AssertRateLimitedAsync(limited);
        Assert.Equal(HttpStatusCode.Accepted, unaffected.StatusCode);
        var rejected = Assert.Single(Fixture.Factory.LogSink.Named(SecurityLog.RateLimitRejectedEvent));
        Assert.Equal(RateLimitPolicies.ClientErrors, CapturingLogSink.Scalar(rejected, "Policy"));
        Assert.Equal("user", CapturingLogSink.Scalar(rejected, "PartitionKind"));
        Assert.Equal(ClientErrorsPath, CapturingLogSink.Scalar(rejected, "RoutePattern"));
        Assert.Equal(RateLimitPolicies.ClientErrorLimit + 1, Fixture.Factory.LogSink.Named(SecurityLog.ClientErrorReportedEvent).Count);
    }

    [Fact]
    public async Task InvalidReportsCountAgainstTheLimitTooAndAreNotLogged()
    {
        using var client = ClientFor(await AdministratorAsync());
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 1; attempt <= RateLimitPolicies.ClientErrorLimit; attempt++)
        {
            using var response = await client.PostAsync(ClientErrorsPath, Json("{}"));
            statuses.Add(response.StatusCode);
        }

        using var limited = await client.PostAsync(ClientErrorsPath, Json(ValidBody().ToJsonString()));

        Assert.Equal(Enumerable.Repeat(HttpStatusCode.BadRequest, RateLimitPolicies.ClientErrorLimit), statuses);
        await RateLimitTests.AssertRateLimitedAsync(limited);
        Assert.Empty(Fixture.Factory.LogSink.Named(SecurityLog.ClientErrorReportedEvent));
    }

    [Fact]
    public async Task ABodyOneByteOver8KilobytesGets413AndOneOfExactly8KilobytesReachesValidationOnKestrel()
    {
        await using var factory = Fixture.CreateFactory(factory => factory.UseKestrel(0));
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, UserRole.Administrator);
        var limit = (int)DiagnosticsEndpoints.RequestBodyLimit;

        using var over = await client.PostAsync(ClientErrorsPath, BodyOfLength(limit + 1));
        using var atLimit = await client.PostAsync(ClientErrorsPath, BodyOfLength(limit));

        Assert.Equal(8192, limit);
        await ApiClient.AssertProblemAsync(over, HttpStatusCode.RequestEntityTooLarge, "request.too-large");
        await ApiClient.AssertFieldErrorsAsync(atLimit, "stack");
        Assert.Empty(factory.LogSink.Named(SecurityLog.ClientErrorReportedEvent));
    }

    [Fact]
    public async Task AMissingBodyGets400AndAnotherMediaTypeGets415()
    {
        using var client = ClientFor(await AdministratorAsync());

        using var missing = await client.PostAsync(ClientErrorsPath);
        using var text = await client.PostAsync(ClientErrorsPath, new StringContent(ValidBody().ToJsonString(), Encoding.UTF8, "text/plain"));

        await ApiClient.AssertFieldErrorsAsync(missing, "body");
        await ApiClient.AssertProblemAsync(text, HttpStatusCode.UnsupportedMediaType, "request.unsupported-media-type");
    }

    private static void VerboseLogging(ApiFactory factory)
    {
        factory.Settings["Serilog:MinimumLevel:Default"] = "Verbose";
        foreach (var source in new[] { "Microsoft.AspNetCore", "Microsoft.AspNetCore.Hosting.Diagnostics", "Microsoft.EntityFrameworkCore", "System.Net.Http" })
        {
            factory.Settings[$"Serilog:MinimumLevel:Override:{source}"] = "Verbose";
        }
    }

    private static object? ValueOf(LogEvent logEvent, string property)
    {
        Assert.True(logEvent.Properties.TryGetValue(property, out var value), $"The event has no {property} property.");
        return Assert.IsType<ScalarValue>(value).Value;
    }

    private static IReadOnlyList<string> TemplatePropertiesOf(LogEvent logEvent) =>
        [.. logEvent.MessageTemplate.Tokens.OfType<PropertyToken>().Select(token => token.PropertyName)];

    private static string? EventIdOf(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue("EventId", out var eventId) && eventId is StructureValue structure
            ? structure.Properties.Where(property => property.Name == "Id").Select(property => property.Value.ToString()).FirstOrDefault()
            : null;

    private static JsonObject ValidBody() => new()
    {
        ["message"] = "Cannot read properties of undefined (reading 'number')",
        ["source"] = "app.errorHandler",
        ["route"] = "/jobs/01a15488-9680-7989-92af-ed07fc34a890",
        ["correlationId"] = null,
        ["stack"] = "TypeError: Cannot read properties of undefined (reading 'number')\n    at JobHeader.vue:42:18",
        ["occurredAt"] = "2026-10-19T14:43:10.214Z",
        ["appVersion"] = "1.0.0",
    };

    private static string Text(string value) => JsonSerializer.Serialize(value);

    private static string LengthMessage(int maximum) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, LengthMessageFormat, maximum);

    private static StringContent BodyOfLength(int length)
    {
        const string Template = """{"message":"m","source":"app.errorHandler","occurredAt":"2026-10-19T14:43:10.214Z","stack":""}""";
        var padding = new string('k', length - Template.Length);
        return Json(Template.Insert(Template.Length - 2, padding));
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private Task<User> AdministratorAsync() => new TestData(Fixture, Now).CreateAdministratorAsync(CancellationToken);

    private ApiClient ClientFor(User user)
    {
        var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TestTokens.For(Fixture.Factory, user);
        return client;
    }
}
