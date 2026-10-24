using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.Net.Http.Headers;
using Serilog.Events;

namespace FieldService.Api.Tests.Security;

[Collection(DatabaseCollection.Name)]
public sealed class RateLimitTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string ProtectedPath = "/api/jobs";

    private const int SignInLimit = 10;

    private const int RefreshLimit = 30;

    private const int AnonymousLimit = 60;

    private const int UserLimit = 300;

    private const int ClientErrorLimit = 10;

    private const string ClientErrorsPath = "/api/client-errors";

    [Fact]
    public async Task TheEleventhSignInWithinAMinuteFromOneAddressGets429AndAnotherAddressDoesNot()
    {
        using var client = Fixture.Factory.CreateApiClient();
        using var otherAddress = Fixture.Factory.CreateApiClient();

        for (var attempt = 1; attempt <= SignInLimit; attempt++)
        {
            using var allowed = await SignInUnknownAccountAsync(client);
            Assert.True(allowed.StatusCode == HttpStatusCode.Unauthorized, $"Sign-in {attempt} returned {(int)allowed.StatusCode}.");
        }

        using var eleventh = await SignInUnknownAccountAsync(client);
        using var elsewhere = await SignInUnknownAccountAsync(otherAddress);

        await AssertRateLimitedAsync(eleventh);
        Assert.Equal(HttpStatusCode.Unauthorized, elsewhere.StatusCode);
    }

    [Fact]
    public async Task TheThirtyFirstRefreshWithinAMinuteFromOneAddressGets429()
    {
        using var client = Fixture.Factory.CreateApiClient();

        for (var attempt = 1; attempt <= RefreshLimit; attempt++)
        {
            using var allowed = await client.PostAsync(ApiClient.RefreshPath);
            Assert.True(allowed.StatusCode == HttpStatusCode.Unauthorized, $"Refresh {attempt} returned {(int)allowed.StatusCode}.");
        }

        using var limited = await client.PostAsync(ApiClient.RefreshPath);

        await AssertRateLimitedAsync(limited);
        Assert.Empty(ApiClient.SetCookieHeadersOf(limited));
    }

    [Theory]
    [InlineData("forged")]
    [InlineData("garbage")]
    [InlineData("none")]
    public async Task SixtyOneRequestsWithAnInvalidOrMissingBearerTokenFromOneAddressGet401SixtyTimesThen429(string token)
    {
        using var client = Fixture.Factory.CreateApiClient();
        using var otherAddress = Fixture.Factory.CreateApiClient();
        client.AccessToken = token switch
        {
            "forged" => AccessTokenValidationTests.ForgedToken(Fixture.Factory.Clock.GetUtcNow()),
            "garbage" => "not-a-token",
            _ => null,
        };
        otherAddress.AccessToken = client.AccessToken;
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 1; attempt <= AnonymousLimit + 1; attempt++)
        {
            using var response = await client.GetAsync(ProtectedPath);
            statuses.Add(response.StatusCode);
            if (attempt == AnonymousLimit + 1)
            {
                await AssertRateLimitedAsync(response);
            }
        }

        using var elsewhere = await otherAddress.GetAsync(ProtectedPath);

        Assert.Equal(Enumerable.Repeat(HttpStatusCode.Unauthorized, AnonymousLimit), statuses.Take(AnonymousLimit));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
        Assert.Equal(HttpStatusCode.Unauthorized, elsewhere.StatusCode);
    }

    [Fact]
    public async Task TheThreeHundredFirstRequestOfOneUserGets429FromAnyAddressWhileAnotherUserIsNotAffected()
    {
        using var client = Fixture.Factory.CreateApiClient();
        using var sameUserElsewhere = Fixture.Factory.CreateApiClient();
        using var otherUser = Fixture.Factory.CreateApiClient();
        var userId = Guid.CreateVersion7();
        client.AccessToken = TestTokens.Issue(Fixture.Factory, userId, UserRole.Technician).Value;
        sameUserElsewhere.AccessToken = TestTokens.Issue(Fixture.Factory, userId, UserRole.Technician).Value;
        otherUser.AccessToken = TestTokens.For(Fixture.Factory, UserRole.Technician);
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 1; attempt <= UserLimit; attempt++)
        {
            using var response = await client.GetAsync(ProtectedPath);
            statuses.Add(response.StatusCode);
        }

        using var limited = await client.GetAsync(ProtectedPath);
        using var limitedElsewhere = await sameUserElsewhere.GetAsync(ProtectedPath);
        using var unaffected = await otherUser.GetAsync(ProtectedPath);

        Assert.Equal(Enumerable.Repeat(HttpStatusCode.Forbidden, UserLimit), statuses);
        await AssertRateLimitedAsync(limited);
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedElsewhere.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unaffected.StatusCode);
        var rejected = Assert.Single(Fixture.Factory.LogSink.Named(SecurityLog.RateLimitRejectedEvent));
        Assert.Equal("global", CapturingLogSink.Scalar(rejected, "Policy"));
        Assert.Equal("user", CapturingLogSink.Scalar(rejected, "PartitionKind"));
        Assert.Equal(userId.ToString(), CapturingLogSink.Scalar(rejected, "UserId"));
        Assert.True(IsAbsentOrNull(rejected, "ClientIp"), "A user partition rejection carries a client address.");
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/openapi/v1.json")]
    public async Task HealthAndTheOpenApiDocumentAreNeverLimitedAndDoNotSpendTheAddressBudget(string path)
    {
        using var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = "not-a-token";
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 1; attempt <= AnonymousLimit + 10; attempt++)
        {
            using var response = await client.GetAsync(path);
            statuses.Add(response.StatusCode);
        }

        using var api = await client.GetAsync(ProtectedPath);

        Assert.Equal(Enumerable.Repeat(HttpStatusCode.OK, AnonymousLimit + 10), statuses);
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
    }

    [Fact]
    public async Task RateLimitRejectedIsLoggedOncePerPartitionAndWindowWithTheAddressOfTheAnonymousPartition()
    {
        await using var factory = Fixture.CreateFactory();
        using var client = factory.CreateApiClient();
        using var otherAddress = factory.CreateApiClient();
        client.AccessToken = "not-a-token";
        otherAddress.AccessToken = "not-a-token";

        for (var attempt = 1; attempt <= AnonymousLimit + 3; attempt++)
        {
            using var response = await client.GetAsync(ProtectedPath);
        }

        var firstWindow = factory.LogSink.Named(SecurityLog.RateLimitRejectedEvent);
        factory.Clock.Advance(TimeSpan.FromSeconds(61));
        using var nextWindow = await client.GetAsync(ProtectedPath);
        for (var attempt = 1; attempt <= AnonymousLimit + 1; attempt++)
        {
            using var response = await otherAddress.GetAsync(ProtectedPath);
        }

        var rejected = factory.LogSink.Named(SecurityLog.RateLimitRejectedEvent);
        var first = Assert.Single(firstWindow);
        Assert.Equal(LogEventLevel.Warning, first.Level);
        Assert.Equal("global", CapturingLogSink.Scalar(first, "Policy"));
        Assert.Equal("ip", CapturingLogSink.Scalar(first, "PartitionKind"));
        Assert.Equal(client.RemoteAddress.ToString(), CapturingLogSink.Scalar(first, "ClientIp"));
        Assert.True(IsAbsentOrNull(first, "UserId"), "An address partition rejection carries a user id.");
        Assert.Equal("/api/jobs", CapturingLogSink.Scalar(first, "RoutePattern"));
        Assert.Equal(HttpStatusCode.TooManyRequests, nextWindow.StatusCode);
        Assert.Equal(2, rejected.Count(logEvent => CapturingLogSink.Scalar(logEvent, "ClientIp") == client.RemoteAddress.ToString()));
        Assert.Single(rejected, logEvent => CapturingLogSink.Scalar(logEvent, "ClientIp") == otherAddress.RemoteAddress.ToString());
    }

    [Fact]
    public async Task TheEleventhClientErrorReportOfOneAdministratorWithinAMinuteGets429FromAnyAddressWhileAnotherAdministratorIsNotAffected()
    {
        var data = new TestData(Fixture, Fixture.Factory.Clock.GetUtcNow());
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var otherAdministrator = await data.CreateAdministratorAsync(CancellationToken);
        using var client = Fixture.Factory.CreateApiClient();
        using var sameUserElsewhere = Fixture.Factory.CreateApiClient();
        using var otherUser = Fixture.Factory.CreateApiClient();
        client.AccessToken = TestTokens.For(Fixture.Factory, administrator);
        sameUserElsewhere.AccessToken = TestTokens.For(Fixture.Factory, administrator);
        otherUser.AccessToken = TestTokens.For(Fixture.Factory, otherAdministrator);
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 1; attempt <= ClientErrorLimit; attempt++)
        {
            using var response = await client.PostJsonAsync(ClientErrorsPath, RoleMatrixTests.ClientErrorBody(data.Now));
            statuses.Add(response.StatusCode);
        }

        using var limited = await client.PostJsonAsync(ClientErrorsPath, RoleMatrixTests.ClientErrorBody(data.Now));
        using var limitedElsewhere = await sameUserElsewhere.PostJsonAsync(ClientErrorsPath, RoleMatrixTests.ClientErrorBody(data.Now));
        using var unaffected = await otherUser.PostJsonAsync(ClientErrorsPath, RoleMatrixTests.ClientErrorBody(data.Now));
        using var otherEndpoint = await client.GetAsync(ProtectedPath);

        Assert.Equal(Enumerable.Repeat(HttpStatusCode.Accepted, ClientErrorLimit), statuses);
        await AssertRateLimitedAsync(limited);
        await AssertRateLimitedAsync(limitedElsewhere);
        Assert.Equal(HttpStatusCode.Accepted, unaffected.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherEndpoint.StatusCode);
        var rejected = Assert.Single(Fixture.Factory.LogSink.Named(SecurityLog.RateLimitRejectedEvent));
        Assert.Equal(LogEventLevel.Warning, rejected.Level);
        Assert.Equal("client-errors", CapturingLogSink.Scalar(rejected, "Policy"));
        Assert.Equal("user", CapturingLogSink.Scalar(rejected, "PartitionKind"));
        Assert.Equal(administrator.Id.ToString(), CapturingLogSink.Scalar(rejected, "UserId"));
        Assert.Equal(ClientErrorsPath, CapturingLogSink.Scalar(rejected, "RoutePattern"));
        Assert.True(IsAbsentOrNull(rejected, "ClientIp"), "A user partition rejection carries a client address.");
        Assert.Equal(ClientErrorLimit + 1, Fixture.Factory.LogSink.Named(SecurityLog.ClientErrorReportedEvent).Count);
    }

    [Fact]
    public async Task AClientErrorReportRefusedWith429IsNeverLoggedOrEchoedAndTheRejectionIsLoggedOnceForThePartition()
    {
        var data = new TestData(Fixture, Fixture.Factory.Clock.GetUtcNow());
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        using var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TestTokens.For(Fixture.Factory, administrator);
        var marker = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        var refused = new
        {
            message = $"Refused report {marker} for qa.{marker}@fieldservice.example",
            source = $"app.errorHandler {marker}",
            route = $"/refused/{marker}",
            stack = $"at refused ({marker})",
            occurredAt = ApiClient.WireInstant(data.Now),
        };

        for (var attempt = 1; attempt <= ClientErrorLimit; attempt++)
        {
            using var accepted = await client.PostJsonAsync(ClientErrorsPath, RoleMatrixTests.ClientErrorBody(data.Now));
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        }

        var texts = new List<string>();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var limited = await client.PostJsonAsync(ClientErrorsPath, refused);
            texts.Add(await limited.Content.ReadAsStringAsync(CancellationToken));
            await AssertRateLimitedAsync(limited);
        }

        Assert.All(texts, text => Assert.DoesNotContain(marker, text, StringComparison.Ordinal));
        Assert.Equal(ClientErrorLimit, Fixture.Factory.LogSink.Named(SecurityLog.ClientErrorReportedEvent).Count);
        Assert.All(Fixture.Factory.LogSink.Events, logEvent =>
            Assert.DoesNotContain(marker, CapturingLogSink.Text(logEvent), StringComparison.Ordinal));
        var rejected = Assert.Single(Fixture.Factory.LogSink.Named(SecurityLog.RateLimitRejectedEvent));
        Assert.Equal("client-errors", CapturingLogSink.Scalar(rejected, "Policy"));
        Assert.Equal(administrator.Id.ToString(), CapturingLogSink.Scalar(rejected, "UserId"));
    }

    internal static async Task AssertRateLimitedAsync(HttpResponseMessage response)
    {
        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.TooManyRequests, "rate-limit.exceeded");
        var retryAfter = Assert.Single(response.Headers.GetValues(HeaderNames.RetryAfter));
        Assert.Matches("^[0-9]+$", retryAfter);
        var seconds = int.Parse(retryAfter, CultureInfo.InvariantCulture);
        Assert.InRange(seconds, 1, 60);
        Assert.Equal("Too many requests.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("https://tools.ietf.org/html/rfc6585#section-4", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.String, problem.RootElement.GetProperty("detail").ValueKind);
        Assert.True(response.Headers.CacheControl?.NoStore, "Cache-Control: no-store is missing on the 429.");
    }

    public static bool IsAbsentOrNull(LogEvent logEvent, string property) =>
        !logEvent.Properties.TryGetValue(property, out var value)
        || value is ScalarValue { Value: null };

    public static Task<HttpResponseMessage> SignInUnknownAccountAsync(ApiClient client) =>
        client.SignInAsync($"nobody-{Guid.NewGuid():N}@fieldservice.example", TestData.Password, SessionClient.Web);
}
