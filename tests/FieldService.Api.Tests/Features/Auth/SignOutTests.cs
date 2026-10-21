using System.Net;
using System.Text;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Serilog.Events;

namespace FieldService.Api.Tests.Features.Auth;

[Collection(DatabaseCollection.Name)]
public sealed class SignOutTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task AWebSignOutRevokesTheSessionClearsTheCookieAndEndsLaterRefreshes()
    {
        using var signedIn = await SignInAsync(Fixture.Factory, SessionClient.Web);

        using var response = await signedIn.SignOutAsync();
        using var refresh = await signedIn.RefreshAsync();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, response.Content.Headers.ContentLength ?? 0);
        Assert.Null(response.RequestMessage!.Content);
        ApiClient.AssertRefreshCookieCleared(response);
        await AssertSignedOutAsync();
        using var problem = await ApiClient.AssertProblemAsync(refresh, HttpStatusCode.Unauthorized, "auth.session-expired");
    }

    [Fact]
    public async Task AMobileSignOutWithTheBodyTokenRevokesTheSessionWithoutACookie()
    {
        using var signedIn = await SignInAsync(Fixture.Factory, SessionClient.Mobile);

        using var response = await signedIn.SignOutAsync();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(ApiClient.SetCookieHeadersOf(response));
        await AssertSignedOutAsync();
    }

    [Theory]
    [InlineData(SessionClient.Web, "not-a-token")]
    [InlineData(SessionClient.Web, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(SessionClient.Mobile, "")]
    [InlineData(SessionClient.Mobile, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task AMalformedOrUnknownCredentialStillGets204AndAPresentedCookieIsCleared(SessionClient channel, string token)
    {
        using var client = Fixture.Factory.CreateApiClient();

        using var response = channel == SessionClient.Web
            ? await client.SignOutWithCookieAsync(token)
            : await client.SignOutWithBodyAsync(token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        if (channel == SessionClient.Web)
        {
            ApiClient.AssertRefreshCookieCleared(response);
        }
        else
        {
            Assert.Empty(ApiClient.SetCookieHeadersOf(response));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ASignOutWithoutAnyCredentialStillGets204WithoutSetCookie(bool csrfHeader)
    {
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.SignOutWithCookieAsync(null, csrfHeader);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(ApiClient.SetCookieHeadersOf(response));
    }

    [Fact]
    public async Task ACrossSitePostWithNoBodyNoContentTypeNoCookieAndNoHeaderGets204WithoutSetCookieOnKestrel()
    {
        await using var factory = Fixture.CreateFactory(factory => factory.UseKestrel(0));
        using var signedIn = await SignInAsync(factory, SessionClient.Web);

        using var forged = await signedIn.Client.PostAsync(ApiClient.SignOutPath, content: null);

        RefreshSessionTests.AssertSentWithoutCredential(forged, mediaType: null);
        Assert.Equal(HttpStatusCode.NoContent, forged.StatusCode);
        Assert.Empty(ApiClient.SetCookieHeadersOf(forged));
        await AssertTheSessionStillRefreshesAndSignsOutAsync(signedIn);
    }

    [Theory]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("text/plain")]
    public async Task ACrossSiteEmptyFormOrTextPostWithoutTheCookieGets415BeforeTheHandlerAndNoSetCookieOnKestrel(string mediaType)
    {
        await using var factory = Fixture.CreateFactory(factory => factory.UseKestrel(0));
        using var signedIn = await SignInAsync(factory, SessionClient.Web);

        using var forged = await signedIn.Client.PostAsync(ApiClient.SignOutPath, RefreshSessionTests.EmptyBodyOf(mediaType));

        RefreshSessionTests.AssertSentWithoutCredential(forged, mediaType);
        using var problem = await ApiClient.AssertProblemAsync(forged, HttpStatusCode.UnsupportedMediaType, "request.unsupported-media-type");
        Assert.Empty(ApiClient.SetCookieHeadersOf(forged));
        await AssertTheSessionStillRefreshesAndSignsOutAsync(signedIn);
    }

    [Fact]
    public async Task AnAlreadyRevokedOrExpiredSessionStillGets204AndClearsTheCookie()
    {
        await using var factory = Fixture.CreateFactory();
        using var revoked = await SignInAsync(factory, SessionClient.Web);
        using var expired = await SignInAsync(factory, SessionClient.Web);
        using var first = await revoked.SignOutAsync();

        using var again = await revoked.SignOutAsync();
        factory.Clock.Advance(TimeSpan.FromMinutes(61));
        using var afterExpiry = await expired.SignOutAsync();

        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        ApiClient.AssertRefreshCookieCleared(again);
        Assert.Equal(HttpStatusCode.NoContent, afterExpiry.StatusCode);
        ApiClient.AssertRefreshCookieCleared(afterExpiry);
    }

    [Fact]
    public async Task TheCookieWithoutTheCsrfHeaderGets403AndTheSessionStaysValid()
    {
        using var signedIn = await SignInAsync(Fixture.Factory, SessionClient.Web);

        using var refused = await signedIn.Client.SignOutWithCookieAsync(signedIn.RefreshToken, csrfHeader: false);

        using var problem = await ApiClient.AssertProblemAsync(refused, HttpStatusCode.Forbidden, "auth.csrf-required");
        Assert.Empty(ApiClient.SetCookieHeadersOf(refused));
        await using (var db = Fixture.CreateAppDbContext())
        {
            Assert.Null((await db.UserSessions.AsNoTracking().SingleAsync(CancellationToken)).RevokedAt);
        }

        using var refresh = await signedIn.RefreshAsync();
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    }

    [Fact]
    public async Task AnEmptyJsonObjectIsAcceptedAndABodyThatIsNotJsonGets415()
    {
        using var signedIn = await SignInAsync(Fixture.Factory, SessionClient.Web);

        using var plainText = await signedIn.Client.PostAsync(ApiClient.SignOutPath, new StringContent("x", Encoding.UTF8, "text/plain"));
        using var emptyObject = await signedIn.Client.PostAsync(
            ApiClient.SignOutPath,
            new StringContent("{}", Encoding.UTF8, "application/json"),
            request =>
            {
                request.Headers.Add(HeaderNames.Cookie, $"{RefreshCookie.Name}={signedIn.RefreshToken}");
                request.Headers.Add(CsrfHeader.Name, CsrfHeader.RequiredValue);
            });

        using var problem = await ApiClient.AssertProblemAsync(plainText, HttpStatusCode.UnsupportedMediaType, "request.unsupported-media-type");
        Assert.Equal(HttpStatusCode.NoContent, emptyObject.StatusCode);
        await AssertSignedOutAsync();
    }

    [Fact]
    public async Task ATokenSentThroughTheOtherChannelDoesNotRevokeItsSession()
    {
        using var web = await SignInAsync(Fixture.Factory, SessionClient.Web);

        using var response = await web.Client.SignOutWithBodyAsync(web.RefreshToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var db = Fixture.CreateAppDbContext();
        Assert.Null((await db.UserSessions.AsNoTracking().SingleAsync(CancellationToken)).RevokedAt);
    }

    [Fact]
    public async Task SignOutLogsTheUserAndSessionIdentifiersOnly()
    {
        await using var factory = Fixture.CreateFactory(factory => factory.Settings["Serilog:MinimumLevel:Default"] = "Information");
        using var signedIn = await SignInAsync(factory, SessionClient.Mobile);

        using var response = await signedIn.SignOutAsync();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var signedOut = Assert.Single(factory.LogSink.Named(SecurityLog.SignOutEvent));
        Assert.Equal(LogEventLevel.Information, signedOut.Level);
        Assert.Equal(signedIn.User.Id.ToString(), CapturingLogSink.Scalar(signedOut, "UserId"));
        Assert.False(string.IsNullOrEmpty(CapturingLogSink.Scalar(signedOut, "SessionId")));
        Assert.All(factory.LogSink.Events, logEvent =>
            Assert.DoesNotContain(signedIn.RefreshToken, CapturingLogSink.Text(logEvent), StringComparison.Ordinal));
    }

    private Task<TestData.SignedInSession> SignInAsync(ApiFactory factory, SessionClient sessionClient) =>
        new TestData(Fixture, factory.Clock.GetUtcNow()).SignInAsync(factory, sessionClient, CancellationToken);

    private async Task AssertTheSessionStillRefreshesAndSignsOutAsync(TestData.SignedInSession signedIn)
    {
        using var refresh = await signedIn.RefreshAsync();
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var next = await signedIn.NextTokenAsync(refresh);
        Assert.NotEqual(signedIn.RefreshToken, next);

        using var signOut = await signedIn.SignOutAsync(next);

        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        ApiClient.AssertRefreshCookieCleared(signOut);
        await using var db = Fixture.CreateAppDbContext();
        var session = await db.UserSessions.AsNoTracking().SingleAsync(CancellationToken);
        Assert.Equal(SessionRevocationReason.SignOut, session.RevokedReason);
    }

    private async Task AssertSignedOutAsync()
    {
        await using var db = Fixture.CreateAppDbContext();
        var session = await db.UserSessions.AsNoTracking().SingleAsync(CancellationToken);
        Assert.Equal(SessionRevocationReason.SignOut, session.RevokedReason);
        Assert.Equal(Fixture.Factory.Clock.GetUtcNow(), session.RevokedAt);
    }
}
