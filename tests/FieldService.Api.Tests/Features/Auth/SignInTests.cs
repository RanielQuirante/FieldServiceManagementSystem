using System.Buffers.Binary;
using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Serilog.Events;

namespace FieldService.Api.Tests.Features.Auth;

[Collection(DatabaseCollection.Name)]
public sealed class SignInTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private TestData Data => new(Fixture, Fixture.Factory.Clock.GetUtcNow());

    [Fact]
    public async Task AnAdministratorOnTheWebGetsASessionWithTheRefreshTokenOnlyInTheCookie()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        var now = Fixture.Factory.Clock.GetUtcNow();
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.SignInAsync(administrator.Email, TestData.Password, SessionClient.Web);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("refreshToken").ValueKind);
        Assert.Equal(ApiClient.WireInstant(now.AddMinutes(15)), body.RootElement.GetProperty("accessTokenExpiresAt").GetString());
        var user = body.RootElement.GetProperty("user");
        Assert.Equal(administrator.Id, user.GetProperty("id").GetGuid());
        Assert.Equal(administrator.DisplayName, user.GetProperty("displayName").GetString());
        Assert.Equal("Administrator", user.GetProperty("role").GetString());
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("accessToken").GetString()));

        var cookie = Assert.IsType<SetCookieHeaderValue>(ApiClient.RefreshCookieOf(response));
        Assert.Matches("^[A-Za-z0-9_-]{43}$", cookie.Value.ToString());
        Assert.Equal(TimeSpan.FromSeconds(3600), cookie.MaxAge);
        Assert.Equal("/api/auth", cookie.Path.ToString());
        Assert.True(cookie.Secure);
        Assert.True(cookie.HttpOnly);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
        Assert.Null(cookie.Expires);
        Assert.False(cookie.Domain.HasValue);
    }

    [Fact]
    public async Task ATechnicianOnMobileGetsTheRefreshTokenInTheBodyAndNoCookie()
    {
        var technician = await Data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.SignInAsync(technician.User.Email, TestData.Password, SessionClient.Mobile);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await ApiClient.ReadSessionAsync(response);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", session.RefreshToken);
        Assert.Equal(technician.UserId, session.User.Id);
        Assert.Equal(UserRole.Technician, session.User.Role);
        Assert.Empty(ApiClient.SetCookieHeadersOf(response));
    }

    [Fact]
    public async Task UnknownEmailWrongPasswordAndInactiveAccountGetTheSameInvalidCredentialsProblem()
    {
        var active = await Data.CreateAdministratorAsync(CancellationToken);
        var inactive = await Data.CreateAdministratorAsync(CancellationToken, isActive: false);
        using var client = Fixture.Factory.CreateApiClient();

        using var unknown = await client.SignInAsync($"nobody-{Guid.NewGuid():N}@fieldservice.example", TestData.Password, SessionClient.Web);
        using var wrongPassword = await client.SignInAsync(active.Email, $"{TestData.Password}x", SessionClient.Web);
        using var inactiveAccount = await client.SignInAsync(inactive.Email, TestData.Password, SessionClient.Web);

        var bodies = new List<string>();
        foreach (var response in new[] { unknown, wrongPassword, inactiveAccount })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(ApiClient.ProblemMediaType, response.Content.Headers.ContentType?.MediaType);
            Assert.Empty(ApiClient.SetCookieHeadersOf(response));
            Assert.False(response.Headers.Contains(HeaderNames.WWWAuthenticate));
            bodies.Add(await WithoutRequestIdentifiersAsync(response));
        }

        Assert.Single(bodies.Distinct(StringComparer.Ordinal));
        using var problem = JsonDocument.Parse(bodies[0]);
        Assert.Equal("auth.invalid-credentials", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal("The email or password is incorrect.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(0, await CountSessionsAsync());
    }

    [Fact]
    public async Task TheWrongApplicationGets403OnlyAfterThePasswordVerifies()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        var technician = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        using var client = Fixture.Factory.CreateApiClient();

        using var technicianOnWeb = await client.SignInAsync(technician.User.Email, TestData.Password, SessionClient.Web);
        using var administratorOnMobile = await client.SignInAsync(administrator.Email, TestData.Password, SessionClient.Mobile);
        using var wrongPasswordOnWrongClient = await client.SignInAsync(technician.User.Email, $"{TestData.Password}-wrong", SessionClient.Web);

        await ApiClient.AssertProblemAsync(technicianOnWeb, HttpStatusCode.Forbidden, "auth.wrong-application");
        await ApiClient.AssertProblemAsync(administratorOnMobile, HttpStatusCode.Forbidden, "auth.wrong-application");
        await ApiClient.AssertProblemAsync(wrongPasswordOnWrongClient, HttpStatusCode.Unauthorized, "auth.invalid-credentials");
        Assert.Empty(ApiClient.SetCookieHeadersOf(technicianOnWeb));
        Assert.Equal(0, await CountSessionsAsync());
    }

    [Fact]
    public async Task EmailMatchingIgnoresCaseAndSurroundingWhiteSpace()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.SignInAsync($"  {administrator.Email.ToUpperInvariant()} ", TestData.Password, SessionClient.Web);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(SessionClient.Web, 60, 12 * 60)]
    [InlineData(SessionClient.Mobile, 7 * 24 * 60, 30 * 24 * 60)]
    public async Task SignInCreatesOneSessionWithTheClientsExpiryAndRecordsTheSignInTime(
        SessionClient sessionClient,
        int idleMinutes,
        int absoluteMinutes)
    {
        var user = sessionClient == SessionClient.Web
            ? await Data.CreateAdministratorAsync(CancellationToken)
            : (await Data.CreateTechnicianAsync(Trade.Plumbing, CancellationToken)).User;
        var now = Fixture.Factory.Clock.GetUtcNow();
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.SignInAsync(user.Email, TestData.Password, sessionClient);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var issued = sessionClient == SessionClient.Web
            ? ApiClient.RefreshCookieOf(response)!.Value.ToString()
            : (await ApiClient.ReadSessionAsync(response)).RefreshToken!;
        await using var db = Fixture.CreateAppDbContext();
        var session = await db.UserSessions.AsNoTracking().SingleAsync(CancellationToken);
        Assert.Equal(user.Id, session.UserId);
        Assert.Equal(sessionClient, session.Client);
        Assert.Equal(now, session.CreatedAt);
        Assert.Equal(now.AddMinutes(idleMinutes), session.IdleExpiresAt);
        Assert.Equal(now.AddMinutes(absoluteMinutes), session.AbsoluteExpiresAt);
        Assert.Null(session.RevokedAt);
        var token = await db.RefreshTokens.AsNoTracking().SingleAsync(CancellationToken);
        Assert.Equal(session.Id, token.SessionId);
        Assert.Equal(SHA256.HashData(Base64Url.DecodeFromChars(issued)), token.TokenHash);
        Assert.Null(token.ConsumedAt);
        var stored = await db.Users.AsNoTracking().SingleAsync(entry => entry.Id == user.Id, CancellationToken);
        Assert.Equal(now, stored.LastSignedInAt);
    }

    [Fact]
    public async Task AHashWithFewerIterationsIsRehashedAt210000IterationsOnSuccess()
    {
        var weakHasher = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions { IterationCount = 10_000 }));
        var administrator = await Data.CreateAdministratorAsync(
            CancellationToken,
            passwordHash: weakHasher.HashPassword(TestData.NewUser(UserRole.Administrator, Data.Now), TestData.Password));
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.SignInAsync(administrator.Email, TestData.Password, SessionClient.Web);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = Fixture.CreateAppDbContext();
        var stored = await db.Users.AsNoTracking().SingleAsync(entry => entry.Id == administrator.Id, CancellationToken);
        Assert.NotEqual(administrator.PasswordHash, stored.PasswordHash);
        Assert.Equal(10_000, IterationsOf(administrator.PasswordHash));
        Assert.Equal(PasswordVerifier.IterationCount, IterationsOf(stored.PasswordHash));
        using var again = await client.SignInAsync(administrator.Email, TestData.Password, SessionClient.Web);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task AnUnknownEmailStillRunsOneVerificationAgainstTheDummyHash()
    {
        var hasher = new CountingHasher(PasswordVerifier.CreateHasher());
        await using var factory = Fixture.CreateFactory(factory => factory.TestServices = services =>
        {
            services.RemoveAll<IPasswordHasher<User>>();
            services.AddSingleton<IPasswordHasher<User>>(hasher);
        });
        using var client = factory.CreateApiClient();
        _ = factory.Services;
        var before = hasher.Verifications;

        using var response = await client.SignInAsync($"nobody-{Guid.NewGuid():N}@fieldservice.example", TestData.Password, SessionClient.Web);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, hasher.Verifications - before);
        Assert.Equal(1, hasher.Hashes);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("    ")]
    [InlineData("\t \n")]
    public async Task AWhiteSpaceOnlyPasswordIsAnOrdinaryFailedSignInAndAnUnknownEmailStillUsesTheDummyHash(string password)
    {
        var hasher = new CountingHasher(PasswordVerifier.CreateHasher());
        await using var factory = Fixture.CreateFactory(factory => factory.TestServices = services =>
        {
            services.RemoveAll<IPasswordHasher<User>>();
            services.AddSingleton<IPasswordHasher<User>>(hasher);
        });
        var administrator = await new TestData(Fixture, factory.Clock.GetUtcNow()).CreateAdministratorAsync(CancellationToken);
        using var client = factory.CreateApiClient();
        _ = factory.Services;
        var before = hasher.Verifications;

        using var known = await client.SignInAsync(administrator.Email, password, SessionClient.Web);
        var afterKnown = hasher.Verifications;
        using var unknown = await client.SignInAsync($"nobody-{Guid.NewGuid():N}@fieldservice.example", password, SessionClient.Web);

        await ApiClient.AssertProblemAsync(known, HttpStatusCode.Unauthorized, "auth.invalid-credentials");
        await ApiClient.AssertProblemAsync(unknown, HttpStatusCode.Unauthorized, "auth.invalid-credentials");
        Assert.Equal(1, afterKnown - before);
        Assert.Equal(1, hasher.Verifications - afterKnown);
        var failed = factory.LogSink.Named(SecurityLog.SignInFailedEvent);
        Assert.Equal(
            [SecurityLog.WrongPassword, SecurityLog.UnknownAccount],
            failed.Select(logEvent => CapturingLogSink.Scalar(logEvent, "Reason")));
        Assert.Equal(administrator.Id.ToString(), CapturingLogSink.Scalar(failed[0], "UserId"));
        Assert.Empty(ApiClient.SetCookieHeadersOf(known));
        Assert.Equal(0, await CountSessionsAsync());
    }

    [Fact]
    public async Task AWhiteSpaceOnlyPasswordSignsInWhenItIsTheAccountsPassword()
    {
        const string WhiteSpacePassword = "    ";
        var administrator = await Data.CreateAdministratorAsync(
            CancellationToken,
            passwordHash: PasswordVerifier.CreateHasher().HashPassword(TestData.NewUser(UserRole.Administrator, Data.Now), WhiteSpacePassword));
        using var client = Fixture.Factory.CreateApiClient();

        using var exact = await client.SignInAsync(administrator.Email, WhiteSpacePassword, SessionClient.Web);
        using var shorter = await client.SignInAsync(administrator.Email, WhiteSpacePassword[1..], SessionClient.Web);

        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
        await ApiClient.AssertProblemAsync(shorter, HttpStatusCode.Unauthorized, "auth.invalid-credentials");
        Assert.Equal(1, await CountSessionsAsync());
    }

    [Fact]
    public async Task AnEmptyPasswordGets400KeyedPasswordBeforeAnyLookup()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.SignInAsync(administrator.Email, string.Empty, SessionClient.Web);

        await ApiClient.AssertFieldErrorsAsync(response, "password");
        Assert.Empty(Fixture.Factory.LogSink.Named(SecurityLog.SignInFailedEvent));
        Assert.Equal(0, await CountSessionsAsync());
    }

    [Fact]
    public async Task APasswordOneCharacterOverTheMaximumGets400KeyedPassword()
    {
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.SignInAsync($"user-{Guid.NewGuid():N}@fieldservice.example", new string(TestData.Password[0], 257), SessionClient.Web);

        await ApiClient.AssertFieldErrorsAsync(response, "password");
    }

    [Fact]
    public async Task MissingMembersAndAnInvalidEmailGetFieldErrorsKeyedByMember()
    {
        using var client = Fixture.Factory.CreateApiClient();

        using var empty = await client.PostJsonAsync(ApiClient.SignInPath, new { });
        using var invalidEmail = await client.SignInAsync("not-an-email", TestData.Password, SessionClient.Web);

        await ApiClient.AssertFieldErrorsAsync(empty, "client", "email", "password");
        await ApiClient.AssertFieldErrorsAsync(invalidEmail, "email");
    }

    [Theory]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\u0000")]
    [InlineData("\u001f")]
    [InlineData("\u007f")]
    [InlineData("\u0085")]
    [InlineData("\u009f")]
    public async Task AnEmailWithAControlCharacterGets400KeyedEmailEvenWhenTheRestMatchesAnAccount(string control)
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        var at = administrator.Email.IndexOf('@', StringComparison.Ordinal);
        using var client = Fixture.Factory.CreateApiClient();

        foreach (var email in new[]
        {
            $"{control}{administrator.Email}",
            $"{administrator.Email[..at]}{control}{administrator.Email[at..]}",
            $"{administrator.Email}{control}",
        })
        {
            using var response = await client.SignInAsync(email, TestData.Password, SessionClient.Web);

            await ApiClient.AssertFieldErrorsAsync(response, "email");
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
            var messages = problem.RootElement.GetProperty("errors").GetProperty("email").EnumerateArray().Select(message => message.GetString()).ToList();
            Assert.Contains("Remove line breaks, tabs and other control characters.", messages);
            Assert.All(messages, message => Assert.DoesNotContain(administrator.Email[..at], message, StringComparison.OrdinalIgnoreCase));
            Assert.Empty(ApiClient.SetCookieHeadersOf(response));
        }

        Assert.Equal(0, await CountSessionsAsync());
        Assert.Empty(Fixture.Factory.LogSink.Named(SecurityLog.SignInFailedEvent));
    }

    [Fact]
    public async Task AnEmailControlCharacterIsReportedTogetherWithTheOtherFieldErrors()
    {
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.PostJsonAsync(ApiClient.SignInPath, new { email = "user\t@fieldservice.example" });

        await ApiClient.AssertFieldErrorsAsync(response, "client", "email", "password");
    }

    [Fact]
    public async Task APasswordWithControlCharactersAndSurroundingWhiteSpaceIsUsedExactlyAsSent()
    {
        var password = $"\t {TestData.Password}\u0001\n ";
        var administrator = await Data.CreateAdministratorAsync(
            CancellationToken,
            passwordHash: PasswordVerifier.CreateHasher().HashPassword(TestData.NewUser(UserRole.Administrator, Data.Now), password));
        using var client = Fixture.Factory.CreateApiClient();

        using var exact = await client.SignInAsync(administrator.Email, password, SessionClient.Web);
        using var trimmed = await client.SignInAsync(administrator.Email, password.Trim(), SessionClient.Web);
        using var withoutControls = await client.SignInAsync(administrator.Email, $" {TestData.Password}", SessionClient.Web);

        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
        await ApiClient.AssertProblemAsync(trimmed, HttpStatusCode.Unauthorized, "auth.invalid-credentials");
        await ApiClient.AssertProblemAsync(withoutControls, HttpStatusCode.Unauthorized, "auth.invalid-credentials");
        Assert.Equal(1, await CountSessionsAsync());
    }

    [Theory]
    [InlineData("""{"email":"a@fieldservice.example","password":"x","client":"Desktop"}""", "client")]
    [InlineData("""{"email":"a@fieldservice.example","password":"x","client":1}""", "client")]
    [InlineData("""{"email":"a@fieldservice.example","password":5,"client":"Web"}""", "password")]
    [InlineData("""{"email":null,"password":"x","client":"Web"}""", "email")]
    public async Task AWrongJsonTypeOrAnUnknownEnumerationValueGetsAFieldErrorKeyedByItsPath(string json, string key)
    {
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.PostAsync(
            ApiClient.SignInPath,
            new StringContent(json, Encoding.UTF8, "application/json"));

        await ApiClient.AssertFieldErrorsAsync(response, key);
    }

    [Fact]
    public async Task ABodyOneByteOver16KilobytesGets413OnKestrel()
    {
        await using var factory = Fixture.CreateFactory(factory => factory.UseKestrel(0));
        using var client = factory.CreateApiClient();

        using var atLimit = await client.PostAsync(ApiClient.SignInPath, SignInBodyOfLength(16 * 1024));
        using var overLimit = await client.PostAsync(ApiClient.SignInPath, SignInBodyOfLength((16 * 1024) + 1));

        await ApiClient.AssertFieldErrorsAsync(atLimit, "email");
        await ApiClient.AssertProblemAsync(overLimit, HttpStatusCode.RequestEntityTooLarge, "request.too-large");
    }

    public static StringContent SignInBodyOfLength(int length)
    {
        const string Template = """{"email":"","password":"x","client":"Web"}""";
        var padding = new string('a', length - Template.Length);
        return new StringContent(Template.Insert(10, padding), Encoding.UTF8, "application/json");
    }

    [Fact]
    public async Task TheEleventhSignInWithinAMinuteFromOneAddressGets429WithRetryAfterAndOneWarning()
    {
        using var client = Fixture.Factory.CreateApiClient();
        using var otherAddress = Fixture.Factory.CreateApiClient();

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            using var allowed = await client.PostJsonAsync(ApiClient.SignInPath, new { });
            Assert.Equal(HttpStatusCode.BadRequest, allowed.StatusCode);
        }

        using var eleventh = await client.PostJsonAsync(ApiClient.SignInPath, new { });
        using var twelfth = await client.PostJsonAsync(ApiClient.SignInPath, new { });
        using var elsewhere = await otherAddress.PostJsonAsync(ApiClient.SignInPath, new { });

        using var problem = await ApiClient.AssertProblemAsync(eleventh, HttpStatusCode.TooManyRequests, "rate-limit.exceeded");
        var retryAfter = int.Parse(Assert.Single(eleventh.Headers.GetValues(HeaderNames.RetryAfter)), CultureInfo.InvariantCulture);
        Assert.InRange(retryAfter, 1, 60);
        Assert.Equal($"Try again in {retryAfter} seconds.", problem.RootElement.GetProperty("detail").GetString());
        Assert.Equal("Too many requests.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("no-store", eleventh.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.TooManyRequests, twelfth.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, elsewhere.StatusCode);
        var rejected = Assert.Single(Fixture.Factory.LogSink.Named(SecurityLog.RateLimitRejectedEvent));
        Assert.Equal(LogEventLevel.Warning, rejected.Level);
        Assert.Equal("sign-in", CapturingLogSink.Scalar(rejected, "Policy"));
        Assert.Equal("ip", CapturingLogSink.Scalar(rejected, "PartitionKind"));
        Assert.Equal(client.RemoteAddress.ToString(), CapturingLogSink.Scalar(rejected, "ClientIp"));
        Assert.Equal("/api/auth/sign-in", CapturingLogSink.Scalar(rejected, "RoutePattern"));
    }

    [Fact]
    public async Task SignInLogsIdentifiersOnlyAndAnUnknownAccountWithoutItsEmail()
    {
        await using var factory = Fixture.CreateFactory(factory => factory.Settings["Serilog:MinimumLevel:Default"] = "Information");
        var data = new TestData(Fixture, factory.Clock.GetUtcNow());
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var unknownEmail = $"nobody-{Guid.NewGuid():N}@fieldservice.example";
        using var client = factory.CreateApiClient();

        using var success = await client.SignInAsync(administrator.Email, TestData.Password, SessionClient.Web);
        using var failure = await client.SignInAsync(unknownEmail, TestData.Password, SessionClient.Web);

        var session = await ApiClient.ReadSessionAsync(success);
        var succeeded = Assert.Single(factory.LogSink.Events, logEvent => CapturingLogSink.HasEventName(logEvent, SecurityLog.SignInSucceededEvent));
        Assert.Equal(LogEventLevel.Information, succeeded.Level);
        Assert.Equal(administrator.Id.ToString(), CapturingLogSink.Scalar(succeeded, "UserId"));
        Assert.Equal("Administrator", CapturingLogSink.Scalar(succeeded, "Role"));
        Assert.Equal("Web", CapturingLogSink.Scalar(succeeded, "Client"));
        var failed = Assert.Single(factory.LogSink.Events, logEvent => CapturingLogSink.HasEventName(logEvent, SecurityLog.SignInFailedEvent));
        Assert.Equal(LogEventLevel.Warning, failed.Level);
        Assert.Equal(SecurityLog.UnknownAccount, CapturingLogSink.Scalar(failed, "Reason"));
        Assert.Equal(client.RemoteAddress.ToString(), CapturingLogSink.Scalar(failed, "ClientIp"));
        foreach (var logEvent in factory.LogSink.Events)
        {
            var text = CapturingLogSink.Text(logEvent);
            Assert.DoesNotContain(TestData.Password, text, StringComparison.Ordinal);
            Assert.DoesNotContain(unknownEmail, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(administrator.Email, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(administrator.DisplayName, text, StringComparison.Ordinal);
            Assert.DoesNotContain(session.AccessToken, text, StringComparison.Ordinal);
            Assert.DoesNotContain(ApiClient.RefreshCookieOf(success)!.Value.ToString(), text, StringComparison.Ordinal);
        }
    }

    private static int IterationsOf(string hash)
    {
        var bytes = Convert.FromBase64String(hash);
        return (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5, 4));
    }

    private static async Task<string> WithoutRequestIdentifiersAsync(HttpResponseMessage response)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
        node.Remove("correlationId");
        node.Remove("traceId");
        return node.ToJsonString();
    }

    private async Task<int> CountSessionsAsync()
    {
        await using var db = Fixture.CreateAppDbContext();
        return await db.UserSessions.CountAsync(CancellationToken);
    }

    private sealed class CountingHasher(IPasswordHasher<User> inner) : IPasswordHasher<User>
    {
        private int _verifications;

        private int _hashes;

        public int Verifications => Volatile.Read(ref _verifications);

        public int Hashes => Volatile.Read(ref _hashes);

        public string HashPassword(User user, string password)
        {
            Interlocked.Increment(ref _hashes);
            return inner.HashPassword(user, password);
        }

        public PasswordVerificationResult VerifyHashedPassword(User user, string hashedPassword, string providedPassword)
        {
            Interlocked.Increment(ref _verifications);
            return inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }
}
