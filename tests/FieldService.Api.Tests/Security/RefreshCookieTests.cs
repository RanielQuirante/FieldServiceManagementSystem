using System.Globalization;
using System.Net;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;

namespace FieldService.Api.Tests.Security;

[Collection(DatabaseCollection.Name)]
public sealed class RefreshCookieTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    public const string ClearedExpires = "Thu, 01 Jan 1970 00:00:00 GMT";

    private static readonly string[] _setAttributes = ["httponly", "max-age", "path", "samesite", "secure"];

    private static readonly string[] _clearAttributes = ["expires", "httponly", "max-age", "path", "samesite", "secure"];

    [Fact]
    public async Task WebSignInSetsTheCookieWithExactlyTheContractAttributesAndNoDomainOrExpires()
    {
        using var response = await SignInAdministratorAsync(Fixture, Fixture.Factory);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = SingleRefreshCookie(response);
        AssertIssued(cookie, expectedMaxAge: 3600, secure: true);
    }

    [Fact]
    public async Task EveryWebRefreshSetsANewCookieWhoseMaxAgeNeverPassesTheAbsoluteExpiry()
    {
        await using var factory = Fixture.CreateFactory();
        using var client = factory.CreateApiClient();
        using var signIn = await SignInAdministratorAsync(Fixture, factory, client);
        var token = SingleRefreshCookie(signIn).Value;
        var maxAges = new List<long>();
        var tokens = new HashSet<string>(StringComparer.Ordinal) { token };

        for (var step = 1; step <= 14; step++)
        {
            factory.Clock.Advance(TimeSpan.FromMinutes(50));
            using var refresh = await client.RefreshWithCookieAsync(token);
            Assert.True(refresh.StatusCode == HttpStatusCode.OK, $"Refresh {step} returned {(int)refresh.StatusCode}.");
            var cookie = SingleRefreshCookie(refresh);
            AssertIssued(cookie, expectedMaxAge: null, secure: true);
            maxAges.Add(long.Parse(cookie.Attributes["max-age"]!, CultureInfo.InvariantCulture));
            Assert.True(tokens.Add(cookie.Value), $"Refresh {step} reissued an earlier token.");
            token = cookie.Value;
        }

        factory.Clock.Advance(TimeSpan.FromMinutes(20));
        using var expired = await client.RefreshWithCookieAsync(token);

        long[] expected = [3600, 3600, 3600, 3600, 3600, 3600, 3600, 3600, 3600, 3600, 3600, 3600, 3600, 1200];
        Assert.Equal(expected, maxAges);
        using var problem = await ApiClient.AssertProblemAsync(expired, HttpStatusCode.Unauthorized, "auth.session-expired");
        AssertCleared(SingleRefreshCookie(expired), secure: true);
    }

    [Fact]
    public async Task WebSignOutClearsTheCookieWithAnEmptyValueMaxAgeZeroAndTheEpochExpiry()
    {
        using var signedIn = await new TestData(Fixture, Fixture.Factory.Clock.GetUtcNow())
            .SignInAsync(Fixture.Factory, SessionClient.Web, CancellationToken);

        using var signOut = await signedIn.SignOutAsync();

        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        AssertCleared(SingleRefreshCookie(signOut), secure: true);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("malformed")]
    [InlineData("revoked")]
    [InlineData("absent")]
    public async Task AWebRefreshThatFailsWithSessionExpiredClearsTheCookieOnlyWhenTheRequestPresentedOne(string credential)
    {
        using var signedIn = await new TestData(Fixture, Fixture.Factory.Clock.GetUtcNow())
            .SignInAsync(Fixture.Factory, SessionClient.Web, CancellationToken);
        if (credential == "revoked")
        {
            using var signOut = await signedIn.SignOutAsync();
        }

        using var response = await signedIn.Client.RefreshWithCookieAsync(credential switch
        {
            "unknown" => RefreshTokenFactory.Create().Value,
            "malformed" => "not-a-refresh-token",
            "revoked" => signedIn.RefreshToken,
            _ => null,
        });

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.session-expired");
        if (credential == "absent")
        {
            Assert.False(response.RequestMessage!.Headers.Contains(HeaderNames.Cookie));
            Assert.True(response.RequestMessage.Headers.Contains(CsrfHeader.Name));
            Assert.Empty(ApiClient.SetCookieHeadersOf(response));
        }
        else
        {
            AssertCleared(SingleRefreshCookie(response), secure: true);
        }
    }

    [Theory]
    [InlineData(ApiClient.RefreshPath)]
    [InlineData(ApiClient.SignOutPath)]
    public async Task ACsrfRefusalSendsNoSetCookieAndLeavesTheCookieUsable(string path)
    {
        using var signedIn = await new TestData(Fixture, Fixture.Factory.Clock.GetUtcNow())
            .SignInAsync(Fixture.Factory, SessionClient.Web, CancellationToken);

        using var refused = path == ApiClient.RefreshPath
            ? await signedIn.Client.RefreshWithCookieAsync(signedIn.RefreshToken, csrfHeader: false)
            : await signedIn.Client.SignOutWithCookieAsync(signedIn.RefreshToken, csrfHeader: false);
        using var accepted = await signedIn.RefreshAsync();

        using var problem = await ApiClient.AssertProblemAsync(refused, HttpStatusCode.Forbidden, "auth.csrf-required");
        Assert.Empty(ApiClient.SetCookieHeadersOf(refused));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Theory]
    [InlineData("Testing", "http", true)]
    [InlineData("Staging", "http", true)]
    [InlineData("Production", "http", true)]
    [InlineData("Development", "http", false)]
    [InlineData("Development", "https", true)]
    public async Task SecureIsSetInEveryEnvironmentAndInDevelopmentOnlyOverHttps(string environment, string scheme, bool secure)
    {
        await using var factory = Fixture.CreateFactory(factory => EndpointInventoryTests.ConfigureEnvironment(factory, environment));
        using var client = new ApiClient(factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"{scheme}://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        }));

        using var signIn = await SignInAdministratorAsync(Fixture, factory, client);
        var issued = SingleRefreshCookie(signIn);
        using var signOut = await client.SignOutWithCookieAsync(issued.Value);

        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        AssertIssued(issued, expectedMaxAge: 3600, secure);
        AssertCleared(SingleRefreshCookie(signOut), secure);
    }

    [Theory]
    [InlineData("/api/jobs")]
    [InlineData("/api/checklist-templates")]
    public async Task TheRefreshCookieAuthenticatesNoOtherEndpoint(string path)
    {
        using var signedIn = await new TestData(Fixture, Fixture.Factory.Clock.GetUtcNow())
            .SignInAsync(Fixture.Factory, SessionClient.Web, CancellationToken);

        using var response = await signedIn.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, path)
        {
            Headers = { { HeaderNames.Cookie, $"{RefreshCookie.Name}={signedIn.RefreshToken}" }, { CsrfHeader.Name, CsrfHeader.RequiredValue } },
        });

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated");
    }

    public static async Task<HttpResponseMessage> SignInAdministratorAsync(
        DatabaseFixture fixture,
        ApiFactory factory,
        ApiClient? client = null)
    {
        var administrator = await new TestData(fixture, factory.Clock.GetUtcNow())
            .CreateAdministratorAsync(TestContext.Current.CancellationToken);
        if (client is not null)
        {
            return await client.SignInAsync(administrator.Email, TestData.Password, SessionClient.Web);
        }

        using var owned = factory.CreateApiClient();
        return await owned.SignInAsync(administrator.Email, TestData.Password, SessionClient.Web);
    }

    public static RawCookie SingleRefreshCookie(HttpResponseMessage response)
    {
        var headers = ApiClient.SetCookieHeadersOf(response);
        var raw = Assert.Single(headers);
        var cookie = RawCookie.Parse(raw);
        Assert.Equal(RefreshCookie.Name, cookie.Name);
        return cookie;
    }

    internal static void AssertIssued(RawCookie cookie, long? expectedMaxAge, bool secure)
    {
        Assert.Matches("^[A-Za-z0-9_-]{43}$", cookie.Value);
        Assert.Equal(
            secure ? _setAttributes : _setAttributes.Where(name => name != "secure"),
            cookie.Attributes.Keys.Order(StringComparer.Ordinal));
        var maxAge = long.Parse(cookie.Attributes["max-age"]!, CultureInfo.InvariantCulture);
        if (expectedMaxAge is { } expected)
        {
            Assert.Equal(expected, maxAge);
        }

        Assert.InRange(maxAge, 1, 3600);
        AssertCommonAttributes(cookie);
    }

    private static void AssertCleared(RawCookie cookie, bool secure)
    {
        Assert.Equal(string.Empty, cookie.Value);
        Assert.Equal(
            secure ? _clearAttributes : _clearAttributes.Where(name => name != "secure"),
            cookie.Attributes.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("0", cookie.Attributes["max-age"]);
        Assert.Equal(ClearedExpires, cookie.Attributes["expires"]);
        AssertCommonAttributes(cookie);
    }

    private static void AssertCommonAttributes(RawCookie cookie)
    {
        Assert.Equal(RefreshCookie.Path, cookie.Attributes["path"]);
        Assert.Equal("strict", cookie.Attributes["samesite"]?.ToLowerInvariant());
        Assert.Null(cookie.Attributes["httponly"]);
        if (cookie.Attributes.TryGetValue("secure", out var secure))
        {
            Assert.Null(secure);
        }
    }

    public sealed record RawCookie(string Name, string Value, Dictionary<string, string?> Attributes)
    {
        public static RawCookie Parse(string header)
        {
            var parts = header.Split(';').Select(part => part.Trim()).Where(part => part.Length > 0).ToArray();
            var nameValue = parts[0].Split('=', 2);
            var attributes = parts
                .Skip(1)
                .Select(part => part.Split('=', 2))
                .ToDictionary(
                    pair => pair[0].ToLowerInvariant(),
                    pair => pair.Length > 1 ? pair[1] : null,
                    StringComparer.Ordinal);
            return new RawCookie(nameValue[0], nameValue.Length > 1 ? nameValue[1] : string.Empty, attributes);
        }
    }
}
