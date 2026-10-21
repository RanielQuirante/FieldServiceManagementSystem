using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Features.Auth;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Tests.Security;

[Collection(DatabaseCollection.Name)]
public sealed class CsrfHeaderTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    public static TheoryData<string, string> MissingOrWrongHeaders => new()
    {
        { ApiClient.RefreshPath, "(none)" },
        { ApiClient.RefreshPath, "0" },
        { ApiClient.RefreshPath, "true" },
        { ApiClient.RefreshPath, "2" },
        { ApiClient.RefreshPath, "1|1" },
        { ApiClient.SignOutPath, "(none)" },
        { ApiClient.SignOutPath, "0" },
        { ApiClient.SignOutPath, "true" },
        { ApiClient.SignOutPath, "2" },
        { ApiClient.SignOutPath, "1|1" },
    };

    public static TheoryData<string, string> CrossSiteEmptyBodies => new()
    {
        { ApiClient.RefreshPath, "application/x-www-form-urlencoded" },
        { ApiClient.RefreshPath, "text/plain" },
        { ApiClient.SignOutPath, "application/x-www-form-urlencoded" },
        { ApiClient.SignOutPath, "text/plain" },
    };

    [Theory]
    [MemberData(nameof(MissingOrWrongHeaders))]
    public async Task TheCookieWithoutExactlyOneHeaderOfOneGets403AndTheTokenIsNeitherConsumedNorRevoked(string path, string header)
    {
        using var signedIn = await SignInAsync(SessionClient.Web);

        using var refused = await signedIn.Client.PostAsync(path, configure: request =>
        {
            request.Headers.Add(Microsoft.Net.Http.Headers.HeaderNames.Cookie, $"{RefreshCookie.Name}={signedIn.RefreshToken}");
            if (header != "(none)")
            {
                request.Headers.TryAddWithoutValidation(CsrfHeader.Name, header.Split('|'));
            }
        });

        using var problem = await ApiClient.AssertProblemAsync(refused, HttpStatusCode.Forbidden, "auth.csrf-required");
        Assert.Empty(ApiClient.SetCookieHeadersOf(refused));
        await AssertUntouchedAsync(signedIn);
        using var accepted = await signedIn.RefreshAsync();
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task TheMobileBodyTokenNeedsNoCsrfHeaderForRefreshOrSignOut()
    {
        using var signedIn = await SignInAsync(SessionClient.Mobile);

        using var refresh = await signedIn.Client.RefreshWithBodyAsync(signedIn.RefreshToken);
        var next = await signedIn.NextTokenAsync(refresh);
        using var signOut = await signedIn.Client.SignOutWithBodyAsync(next);

        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        Assert.False(refresh.RequestMessage!.Headers.Contains(CsrfHeader.Name));
        await using var db = Fixture.CreateAppDbContext();
        var session = await db.UserSessions.AsNoTracking().SingleAsync(entry => entry.UserId == signedIn.User.Id, CancellationToken);
        Assert.Equal(SessionRevocationReason.SignOut, session.RevokedReason);
    }

    [Theory]
    [InlineData("application/x-www-form-urlencoded", "email=a%40fieldservice.example&client=Web")]
    [InlineData("text/plain", "{\"email\":\"a@fieldservice.example\",\"client\":\"Web\"}")]
    [InlineData("multipart/form-data; boundary=x", "--x--")]
    public async Task ASignInThatIsNotJsonGets415(string mediaType, string content)
    {
        using var client = Fixture.Factory.CreateApiClient();
        var body = new StringContent(content, Encoding.UTF8);
        body.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);

        using var response = await client.PostAsync(ApiClient.SignInPath, body);

        using var problem = await ApiClient.AssertProblemAsync(
            response,
            HttpStatusCode.UnsupportedMediaType,
            "request.unsupported-media-type");
    }

    [Theory]
    [InlineData(ApiClient.RefreshPath)]
    [InlineData(ApiClient.SignOutPath)]
    public async Task ACrossSiteFormPostWithTheCookieLeavesTheTokenUntouched(string path)
    {
        using var signedIn = await SignInAsync(SessionClient.Web);

        using var forged = await signedIn.Client.PostAsync(
            path,
            new FormUrlEncodedContent([new KeyValuePair<string, string>("client", "Web")]),
            request => request.Headers.Add(Microsoft.Net.Http.Headers.HeaderNames.Cookie, $"{RefreshCookie.Name}={signedIn.RefreshToken}"));

        using var problem = await ApiClient.AssertProblemAsync(
            forged,
            HttpStatusCode.UnsupportedMediaType,
            "request.unsupported-media-type");
        Assert.Empty(ApiClient.SetCookieHeadersOf(forged));
        await AssertUntouchedAsync(signedIn);
    }

    [Theory]
    [MemberData(nameof(CrossSiteEmptyBodies))]
    public async Task ACrossSiteEmptyFormOrTextPostWithoutTheCookieOrTheHeaderGets415WithoutSetCookieAndTheVictimStillRefreshes(
        string path,
        string mediaType)
    {
        await using var factory = Fixture.CreateFactory(factory => factory.UseKestrel(0));
        using var victim = await SignInAsync(SessionClient.Web, factory);

        using var forged = await victim.Client.PostAsync(path, RefreshSessionTests.EmptyBodyOf(mediaType));

        RefreshSessionTests.AssertSentWithoutCredential(forged, mediaType);
        using var problem = await ApiClient.AssertProblemAsync(
            forged,
            HttpStatusCode.UnsupportedMediaType,
            "request.unsupported-media-type");
        Assert.Empty(ApiClient.SetCookieHeadersOf(forged));
        await AssertTheVictimStillRefreshesAsync(victim);
    }

    [Theory]
    [InlineData(ApiClient.RefreshPath)]
    [InlineData(ApiClient.SignOutPath)]
    public async Task ACrossSitePostWithNoBodyNoContentTypeNoCookieAndNoHeaderSendsNoSetCookieAndTheVictimStillRefreshes(string path)
    {
        await using var factory = Fixture.CreateFactory(factory => factory.UseKestrel(0));
        using var victim = await SignInAsync(SessionClient.Web, factory);

        using var forged = await victim.Client.PostAsync(path, content: null);

        RefreshSessionTests.AssertSentWithoutCredential(forged, mediaType: null);
        if (path == ApiClient.RefreshPath)
        {
            using var problem = await ApiClient.AssertProblemAsync(forged, HttpStatusCode.Unauthorized, "auth.session-expired");
        }
        else
        {
            Assert.Equal(HttpStatusCode.NoContent, forged.StatusCode);
        }

        Assert.Empty(ApiClient.SetCookieHeadersOf(forged));
        await AssertTheVictimStillRefreshesAsync(victim);
    }

    private async Task<TestData.SignedInSession> SignInAsync(SessionClient sessionClient, ApiFactory? factory = null)
    {
        var host = factory ?? Fixture.Factory;
        return await new TestData(Fixture, host.Clock.GetUtcNow()).SignInAsync(host, sessionClient, CancellationToken);
    }

    private async Task AssertTheVictimStillRefreshesAsync(TestData.SignedInSession victim)
    {
        await AssertUntouchedAsync(victim);

        using var refresh = await victim.RefreshAsync();

        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var rotated = RefreshCookieTests.SingleRefreshCookie(refresh);
        RefreshCookieTests.AssertIssued(rotated, expectedMaxAge: 3600, secure: true);
        Assert.NotEqual(victim.RefreshToken, rotated.Value);
    }

    private async Task AssertUntouchedAsync(TestData.SignedInSession signedIn)
    {
        await using var db = Fixture.CreateAppDbContext();
        var session = await db.UserSessions.AsNoTracking().SingleAsync(entry => entry.UserId == signedIn.User.Id, CancellationToken);
        var tokens = await db.RefreshTokens.AsNoTracking().Where(entry => entry.SessionId == session.Id).ToListAsync(CancellationToken);
        Assert.Null(session.RevokedAt);
        Assert.Null(session.LastRefreshedAt);
        var token = Assert.Single(tokens);
        Assert.Null(token.ConsumedAt);
        Assert.Equal(ApiClient.RefreshTokenHashOf(signedIn.RefreshToken), token.TokenHash);
    }
}
