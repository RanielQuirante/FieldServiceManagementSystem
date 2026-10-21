using System.Net;
using System.Net.Http.Json;
using System.Text;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Npgsql;

namespace FieldService.Api.Tests.Features.Auth;

[Collection(DatabaseCollection.Name)]
public sealed class RefreshSessionTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task AWebRefreshWithTheCookieAndTheHeaderRotatesTheCookieAndIssuesANewAccessToken()
    {
        using var signedIn = await SignInAsync(Fixture.Factory, SessionClient.Web);
        var now = Fixture.Factory.Clock.GetUtcNow();

        using var response = await signedIn.RefreshAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.RequestMessage!.Content);
        var session = await ApiClient.ReadSessionAsync(response);
        Assert.Null(session.RefreshToken);
        Assert.NotEqual(signedIn.Session.AccessToken, session.AccessToken);
        Assert.Equal(signedIn.Session.User, session.User);
        var rotated = ApiClient.RefreshCookieOf(response)!;
        Assert.NotEqual(signedIn.RefreshToken, rotated.Value.ToString());
        Assert.Equal(TimeSpan.FromSeconds(3600), rotated.MaxAge);
        Assert.Equal(RefreshCookie.Path, rotated.Path.ToString());
        Assert.True(rotated.Secure && rotated.HttpOnly);

        await using var db = Fixture.CreateAppDbContext();
        var presentedHash = ApiClient.RefreshTokenHashOf(signedIn.RefreshToken);
        var successorHash = ApiClient.RefreshTokenHashOf(rotated.Value.ToString());
        var presented = await db.RefreshTokens.AsNoTracking().SingleAsync(token => token.TokenHash == presentedHash, CancellationToken);
        var successor = await db.RefreshTokens.AsNoTracking().SingleAsync(token => token.TokenHash == successorHash, CancellationToken);
        Assert.Equal(now, presented.ConsumedAt);
        Assert.Equal(successor.Id, presented.ReplacedById);
        Assert.Null(successor.ConsumedAt);
        var stored = await db.UserSessions.AsNoTracking().SingleAsync(CancellationToken);
        Assert.Equal(now, stored.LastRefreshedAt);
        Assert.Equal(now.AddMinutes(60), stored.IdleExpiresAt);
    }

    [Fact]
    public async Task ARefreshWithNoBodyAndNoContentTypeOrWithAnEmptyJsonObjectIsAccepted()
    {
        using var signedIn = await SignInAsync(Fixture.Factory, SessionClient.Web);

        using var noBody = await signedIn.Client.PostAsync(ApiClient.RefreshPath, content: null, request =>
        {
            request.Headers.Add(HeaderNames.Cookie, $"{RefreshCookie.Name}={signedIn.RefreshToken}");
            request.Headers.Add(CsrfHeader.Name, CsrfHeader.RequiredValue);
        });
        var next = await signedIn.NextTokenAsync(noBody);
        using var emptyObject = await signedIn.Client.PostAsync(
            ApiClient.RefreshPath,
            new StringContent("{}", Encoding.UTF8, "application/json"),
            request =>
            {
                request.Headers.Add(HeaderNames.Cookie, $"{RefreshCookie.Name}={next}");
                request.Headers.Add(CsrfHeader.Name, CsrfHeader.RequiredValue);
            });

        Assert.Equal(HttpStatusCode.OK, noBody.StatusCode);
        Assert.Null(noBody.RequestMessage!.Content);
        Assert.Equal(HttpStatusCode.OK, emptyObject.StatusCode);
    }

    [Fact]
    public async Task AMobileRefreshWithTheBodyTokenRotatesItAndSetsNoCookie()
    {
        using var signedIn = await SignInAsync(Fixture.Factory, SessionClient.Mobile);

        using var response = await signedIn.RefreshAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await ApiClient.ReadSessionAsync(response);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", session.RefreshToken);
        Assert.NotEqual(signedIn.RefreshToken, session.RefreshToken);
        Assert.Empty(ApiClient.SetCookieHeadersOf(response));
        await using var db = Fixture.CreateAppDbContext();
        var stored = await db.UserSessions.AsNoTracking().SingleAsync(CancellationToken);
        Assert.Equal(Fixture.Factory.Clock.GetUtcNow().AddDays(7), stored.IdleExpiresAt);
    }

    [Theory]
    [InlineData(SessionClient.Web, 60)]
    [InlineData(SessionClient.Mobile, 7 * 24 * 60)]
    public async Task ASessionPastItsIdleExpiryGets401SessionExpired(SessionClient sessionClient, int idleMinutes)
    {
        await using var factory = Fixture.CreateFactory();
        using var signedIn = await SignInAsync(factory, sessionClient);

        factory.Clock.Advance(TimeSpan.FromMinutes(idleMinutes) - TimeSpan.FromSeconds(1));
        using var justBefore = await signedIn.RefreshAsync();
        Assert.Equal(HttpStatusCode.OK, justBefore.StatusCode);
        var next = await signedIn.NextTokenAsync(justBefore);
        factory.Clock.Advance(TimeSpan.FromMinutes(idleMinutes));
        using var expired = await signedIn.RefreshAsync(next);

        using var problem = await ApiClient.AssertProblemAsync(expired, HttpStatusCode.Unauthorized, "auth.session-expired");
        AssertCookieClearedOnlyOnTheWeb(expired, sessionClient);
    }

    [Fact]
    public async Task AWebSessionEndsAtItsAbsoluteExpiryAndTheLastCookieNeverOutlivesIt()
    {
        await using var factory = Fixture.CreateFactory();
        using var signedIn = await SignInAsync(factory, SessionClient.Web);
        var cookie = signedIn.RefreshToken;

        TimeSpan? lastMaxAge = null;
        for (var refresh = 0; refresh < 14; refresh++)
        {
            factory.Clock.Advance(TimeSpan.FromMinutes(50));
            using var response = await signedIn.RefreshAsync(cookie);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var rotated = ApiClient.RefreshCookieOf(response)!;
            cookie = rotated.Value.ToString();
            lastMaxAge = rotated.MaxAge;
        }

        factory.Clock.Advance(TimeSpan.FromMinutes(20));
        using var expired = await signedIn.RefreshAsync(cookie);

        Assert.Equal(TimeSpan.FromSeconds(1200), lastMaxAge);
        using var problem = await ApiClient.AssertProblemAsync(expired, HttpStatusCode.Unauthorized, "auth.session-expired");
        ApiClient.AssertRefreshCookieCleared(expired);
    }

    [Fact]
    public async Task AMobileSessionEndsAtItsAbsoluteExpiryOfThirtyDays()
    {
        await using var factory = Fixture.CreateFactory();
        using var signedIn = await SignInAsync(factory, SessionClient.Mobile);
        var token = signedIn.RefreshToken;

        for (var refresh = 0; refresh < 4; refresh++)
        {
            factory.Clock.Advance(TimeSpan.FromDays(6));
            using var response = await signedIn.RefreshAsync(token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            token = await signedIn.NextTokenAsync(response);
        }

        factory.Clock.Advance(TimeSpan.FromDays(6));
        using var expired = await signedIn.RefreshAsync(token);

        using var problem = await ApiClient.AssertProblemAsync(expired, HttpStatusCode.Unauthorized, "auth.session-expired");
    }

    [Fact]
    public async Task ARevokedSessionGets401()
    {
        using var signedIn = await SignInAsync(Fixture.Factory, SessionClient.Web);
        using var signOut = await signedIn.SignOutAsync();
        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);

        using var response = await signedIn.RefreshAsync();

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.session-expired");
        ApiClient.AssertRefreshCookieCleared(response);
    }

    [Fact]
    public async Task AnInactiveUserGets401AndTheSessionIsRevokedAsUserInactive()
    {
        using var signedIn = await SignInAsync(Fixture.Factory, SessionClient.Mobile);
        await using (var db = Fixture.CreateAppDbContext())
        {
            var user = await db.Users.SingleAsync(entry => entry.Id == signedIn.User.Id, CancellationToken);
            user.Deactivate(Fixture.Factory.Clock.GetUtcNow());
            await db.SaveChangesAsync(CancellationToken);
        }

        using var response = await signedIn.RefreshAsync();

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.session-expired");
        await using var check = Fixture.CreateAppDbContext();
        var session = await check.UserSessions.AsNoTracking().SingleAsync(CancellationToken);
        Assert.Equal(SessionRevocationReason.UserInactive, session.RevokedReason);
        Assert.Equal(Fixture.Factory.Clock.GetUtcNow(), session.RevokedAt);
    }

    [Theory]
    [InlineData("too-short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task AMalformedOrUnknownWebCookieWithTheHeaderGets401AndClearsTheCookie(string cookie)
    {
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.RefreshWithCookieAsync(cookie);

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.session-expired");
        ApiClient.AssertRefreshCookieCleared(response);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ARefreshWithoutAnyCredentialGets401WithoutSetCookie(bool csrfHeader)
    {
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.RefreshWithCookieAsync(null, csrfHeader);

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.session-expired");
        Assert.Empty(ApiClient.SetCookieHeadersOf(response));
    }

    [Fact]
    public async Task ACrossSitePostWithNoBodyNoContentTypeNoCookieAndNoHeaderGets401WithoutSetCookieOnKestrel()
    {
        await using var factory = Fixture.CreateFactory(factory => factory.UseKestrel(0));
        using var signedIn = await SignInAsync(factory, SessionClient.Web);

        using var forged = await signedIn.Client.PostAsync(ApiClient.RefreshPath, content: null);
        using var refresh = await signedIn.RefreshAsync();

        AssertSentWithoutCredential(forged, mediaType: null);
        using var problem = await ApiClient.AssertProblemAsync(forged, HttpStatusCode.Unauthorized, "auth.session-expired");
        Assert.Empty(ApiClient.SetCookieHeadersOf(forged));
        AssertRotated(refresh, signedIn.RefreshToken);
    }

    [Theory]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("text/plain")]
    public async Task ACrossSiteEmptyFormOrTextPostWithoutTheCookieGets415BeforeTheHandlerAndNoSetCookieOnKestrel(string mediaType)
    {
        await using var factory = Fixture.CreateFactory(factory => factory.UseKestrel(0));
        using var signedIn = await SignInAsync(factory, SessionClient.Web);

        using var forged = await signedIn.Client.PostAsync(ApiClient.RefreshPath, EmptyBodyOf(mediaType));
        using var refresh = await signedIn.RefreshAsync();

        AssertSentWithoutCredential(forged, mediaType);
        using var problem = await ApiClient.AssertProblemAsync(forged, HttpStatusCode.UnsupportedMediaType, "request.unsupported-media-type");
        Assert.Empty(ApiClient.SetCookieHeadersOf(forged));
        AssertRotated(refresh, signedIn.RefreshToken);
    }

    public static ByteArrayContent EmptyBodyOf(string mediaType)
    {
        var content = new ByteArrayContent([]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
        return content;
    }

    internal static void AssertSentWithoutCredential(HttpResponseMessage response, string? mediaType)
    {
        var request = response.RequestMessage!;
        if (mediaType is null)
        {
            Assert.Null(request.Content);
        }
        else
        {
            Assert.Equal(0, request.Content!.Headers.ContentLength);
            Assert.Equal(mediaType, request.Content.Headers.ContentType?.ToString());
        }

        Assert.False(request.Headers.Contains(HeaderNames.Cookie));
        Assert.False(request.Headers.Contains(CsrfHeader.Name));
    }

    private static void AssertRotated(HttpResponseMessage refresh, string presented)
    {
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var rotated = ApiClient.RefreshCookieOf(refresh)!;
        Assert.NotEqual(presented, rotated.Value.ToString());
        Assert.Equal(TimeSpan.FromSeconds(3600), rotated.MaxAge);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-refresh-token")]
    public async Task AMalformedMobileTokenGets401WithoutACookie(string token)
    {
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.RefreshWithBodyAsync(token);

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.session-expired");
        Assert.Empty(ApiClient.SetCookieHeadersOf(response));
    }

    [Fact]
    public async Task ATokenSentThroughTheOtherClientsChannelGets401AndStaysUsableOnItsOwnChannel()
    {
        using var web = await SignInAsync(Fixture.Factory, SessionClient.Web);
        using var mobile = await SignInAsync(Fixture.Factory, SessionClient.Mobile);

        using var webTokenInBody = await mobile.Client.RefreshWithBodyAsync(web.RefreshToken);
        using var mobileTokenInCookie = await web.Client.RefreshWithCookieAsync(mobile.RefreshToken);
        using var webAgain = await web.RefreshAsync();
        using var mobileAgain = await mobile.RefreshAsync();

        using var first = await ApiClient.AssertProblemAsync(webTokenInBody, HttpStatusCode.Unauthorized, "auth.session-expired");
        using var second = await ApiClient.AssertProblemAsync(mobileTokenInCookie, HttpStatusCode.Unauthorized, "auth.session-expired");
        Assert.Equal(HttpStatusCode.OK, webAgain.StatusCode);
        Assert.Equal(HttpStatusCode.OK, mobileAgain.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData("1|1")]
    public async Task TheCookieWithoutExactlyOneCsrfHeaderOfOneGets403AndLeavesTheTokenUntouched(string header)
    {
        var headerValues = header.Split('|', StringSplitOptions.RemoveEmptyEntries);
        using var signedIn = await SignInAsync(Fixture.Factory, SessionClient.Web);

        using var refused = await signedIn.Client.PostAsync(ApiClient.RefreshPath, configure: request =>
        {
            request.Headers.Add(HeaderNames.Cookie, $"{RefreshCookie.Name}={signedIn.RefreshToken}");
            if (headerValues.Length > 0)
            {
                request.Headers.TryAddWithoutValidation(CsrfHeader.Name, headerValues);
            }
        });

        using var problem = await ApiClient.AssertProblemAsync(refused, HttpStatusCode.Forbidden, "auth.csrf-required");
        Assert.Empty(ApiClient.SetCookieHeadersOf(refused));
        await using (var db = Fixture.CreateAppDbContext())
        {
            var token = await db.RefreshTokens.AsNoTracking().SingleAsync(CancellationToken);
            Assert.Null(token.ConsumedAt);
        }

        using var accepted = await signedIn.RefreshAsync();
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task AnOperatorRevocationSurvivesReenablingTheUser()
    {
        using var signedIn = await SignInAsync(Fixture.Factory, SessionClient.Mobile);
        await using (var migrator = await Fixture.OpenMigratorConnectionAsync(CancellationToken))
        {
            await using var revoke = new NpgsqlCommand(
                """
                UPDATE fieldservice.users SET is_active = false, updated_at = now() WHERE id = @user_id;
                UPDATE fieldservice.user_sessions SET revoked_at = now(), revoked_reason = 'Operator' WHERE user_id = @user_id AND revoked_at IS NULL;
                """,
                migrator);
            revoke.Parameters.AddWithValue("user_id", signedIn.User.Id);
            await revoke.ExecuteNonQueryAsync(CancellationToken);
            await using var reactivate = new NpgsqlCommand(
                "UPDATE fieldservice.users SET is_active = true, updated_at = now() WHERE id = @user_id",
                migrator);
            reactivate.Parameters.AddWithValue("user_id", signedIn.User.Id);
            await reactivate.ExecuteNonQueryAsync(CancellationToken);
        }

        using var response = await signedIn.RefreshAsync();

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.session-expired");
        await using var db = Fixture.CreateAppDbContext();
        var session = await db.UserSessions.AsNoTracking().SingleAsync(CancellationToken);
        Assert.Equal(SessionRevocationReason.Operator, session.RevokedReason);
        Assert.True((await db.Users.AsNoTracking().SingleAsync(entry => entry.Id == signedIn.User.Id, CancellationToken)).IsActive);
    }

    [Fact]
    public async Task TheThirtyFirstRefreshWithinAMinuteFromOneAddressGets429()
    {
        using var client = Fixture.Factory.CreateApiClient();

        for (var attempt = 1; attempt <= 30; attempt++)
        {
            using var allowed = await client.RefreshWithCookieAsync(null);
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        using var limited = await client.RefreshWithCookieAsync(null);

        using var problem = await ApiClient.AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, "rate-limit.exceeded");
        Assert.True(limited.Headers.Contains(HeaderNames.RetryAfter));
        Assert.Empty(ApiClient.SetCookieHeadersOf(limited));
    }

    [Fact]
    public async Task ARefreshBodyThatIsNotJsonGets415AndAMalformedOrOverlongBodyGets400()
    {
        using var client = Fixture.Factory.CreateApiClient();

        using var plainText = await client.PostAsync(ApiClient.RefreshPath, new StringContent("token", Encoding.UTF8, "text/plain"));
        using var malformed = await client.PostAsync(
            ApiClient.RefreshPath,
            new StringContent("not json", Encoding.UTF8, "application/json"));
        using var overlong = await client.PostAsync(
            ApiClient.RefreshPath,
            JsonContent.Create(new { refreshToken = new string('a', 65) }, options: ApiClient.Json));

        using var problem = await ApiClient.AssertProblemAsync(plainText, HttpStatusCode.UnsupportedMediaType, "request.unsupported-media-type");
        await ApiClient.AssertFieldErrorsAsync(malformed, "body");
        await ApiClient.AssertFieldErrorsAsync(overlong, "refreshToken");
    }

    private Task<TestData.SignedInSession> SignInAsync(ApiFactory factory, SessionClient sessionClient) =>
        new TestData(Fixture, factory.Clock.GetUtcNow()).SignInAsync(factory, sessionClient, CancellationToken);

    private static void AssertCookieClearedOnlyOnTheWeb(HttpResponseMessage response, SessionClient sessionClient)
    {
        if (sessionClient == SessionClient.Web)
        {
            ApiClient.AssertRefreshCookieCleared(response);
        }
        else
        {
            Assert.Empty(ApiClient.SetCookieHeadersOf(response));
        }
    }
}
