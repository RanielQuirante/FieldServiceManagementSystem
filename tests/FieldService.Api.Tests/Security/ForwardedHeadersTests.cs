using System.Net;
using System.Net.Http.Json;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Observability;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.Net.Http.Headers;

namespace FieldService.Api.Tests.Security;

[Collection(DatabaseCollection.Name)]
public sealed class ForwardedHeadersTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string Nginx = "172.30.80.10";

    private const string SameSubnet = "172.30.80.11";

    private const string Client = "203.0.113.7";

    private const string ForwardedFor = "X-Forwarded-For";

    private const string ForwardedProto = "X-Forwarded-Proto";

    private const string ForwardedHost = "X-Forwarded-Host";

    [Theory]
    [InlineData("127.0.0.1", Client, Client)]
    [InlineData("::1", Client, Client)]
    [InlineData("10.200.250.1", Client, "10.200.250.1")]
    [InlineData(Nginx, Client, Nginx)]
    public async Task DevelopmentTrustsTheForwardedAddressOnlyFromLoopback(string remoteAddress, string forwardedFor, string expected)
    {
        await using var factory = Fixture.CreateFactory(factory => EndpointInventoryTests.ConfigureEnvironment(factory, "Development"));

        var resolved = await ResolvedAddressAsync(factory, remoteAddress, forwardedFor);

        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData(Nginx, Client, Client)]
    [InlineData(Nginx, "198.51.100.66, 203.0.113.7", Client)]
    [InlineData(Nginx, "192.0.2.1, 198.51.100.66, 203.0.113.7", Client)]
    [InlineData(SameSubnet, Client, SameSubnet)]
    [InlineData("127.0.0.1", Client, "127.0.0.1")]
    [InlineData("::1", Client, "::1")]
    public async Task StagingTrustsOnlyNginxsFixedAddressAndTakesOneHop(string remoteAddress, string forwardedFor, string expected)
    {
        await using var factory = CreateStagingFactory();

        var resolved = await ResolvedAddressAsync(factory, remoteAddress, forwardedFor);

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public async Task AClientOutsideTheProxyListCannotDodgeTheSignInLimitWithSpoofedAddresses()
    {
        await using var factory = CreateStagingFactory();
        using var http = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 1; attempt <= 11; attempt++)
        {
            using var response = await SendFromAsync(
                http,
                HttpMethod.Post,
                ApiClient.SignInPath,
                SameSubnet,
                [(ForwardedFor, $"198.51.100.{attempt}")],
                JsonContent.Create(new { }));
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(Enumerable.Repeat(HttpStatusCode.BadRequest, 10), statuses.Take(10));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task BehindNginxEachForwardedClientHasItsOwnSignInPartition()
    {
        await using var factory = CreateStagingFactory();
        using var http = factory.CreateClient();
        var distinct = new List<HttpStatusCode>();
        var repeated = new List<HttpStatusCode>();

        for (var attempt = 1; attempt <= 11; attempt++)
        {
            using var response = await SendFromAsync(
                http,
                HttpMethod.Post,
                ApiClient.SignInPath,
                Nginx,
                [(ForwardedFor, $"198.51.100.{attempt}")],
                JsonContent.Create(new { }));
            distinct.Add(response.StatusCode);
        }

        for (var attempt = 1; attempt <= 11; attempt++)
        {
            using var response = await SendFromAsync(
                http,
                HttpMethod.Post,
                ApiClient.SignInPath,
                Nginx,
                [(ForwardedFor, "192.0.2.200")],
                JsonContent.Create(new { }));
            repeated.Add(response.StatusCode);
        }

        Assert.Equal(Enumerable.Repeat(HttpStatusCode.BadRequest, 11), distinct);
        Assert.Equal(Enumerable.Repeat(HttpStatusCode.BadRequest, 10), repeated.Take(10));
        Assert.Equal(HttpStatusCode.TooManyRequests, repeated[^1]);
    }

    [Theory]
    [InlineData("localhost", false)]
    [InlineData("fieldservice.example", true)]
    public async Task XForwardedHostIsNeverApplied(string requestHost, bool expectedHsts)
    {
        await using var factory = CreateStagingFactory();
        using var http = SecurityHeadersTests.ClientFor(factory, $"http://{requestHost}");

        using var response = await SendFromAsync(
            http,
            HttpMethod.Get,
            "/health/live",
            Nginx,
            [(ForwardedProto, "https"), (ForwardedHost, "fieldservice.example")]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedHsts, response.Headers.Contains(HeaderNames.StrictTransportSecurity));
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.200.250.2", false)]
    public async Task DevelopmentTakesTheForwardedSchemeOnlyFromLoopback(string remoteAddress, bool secureCookie)
    {
        await using var factory = Fixture.CreateFactory(factory => EndpointInventoryTests.ConfigureEnvironment(factory, "Development"));
        var administrator = await new TestData(Fixture, factory.Clock.GetUtcNow()).CreateAdministratorAsync(CancellationToken);
        using var http = factory.CreateClient();

        using var response = await SendFromAsync(
            http,
            HttpMethod.Post,
            ApiClient.SignInPath,
            remoteAddress,
            [(ForwardedProto, "https")],
            JsonContent.Create(new { email = administrator.Email, password = TestData.Password, client = "Web" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = RefreshCookieTests.SingleRefreshCookie(response);
        Assert.Equal(secureCookie, cookie.Attributes.ContainsKey("secure"));
    }

    public static async Task<HttpResponseMessage> SendFromAsync(
        HttpClient http,
        HttpMethod method,
        string path,
        string remoteAddress,
        (string Name, string Value)[] headers,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add(TestClientIpStartupFilter.HeaderName, remoteAddress);
        request.Headers.Add(CorrelationId.HeaderName, $"test-{Guid.NewGuid():N}");
        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        return await http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private ApiFactory CreateStagingFactory() => Fixture.CreateFactory(factory =>
    {
        EndpointInventoryTests.ConfigureEnvironment(factory, "Staging");
        factory.Settings["ForwardedHeaders:KnownProxies:0"] = Nginx;
    });

    private async Task<string?> ResolvedAddressAsync(ApiFactory factory, string remoteAddress, string forwardedFor)
    {
        using var http = factory.CreateClient();
        using var response = await SendFromAsync(
            http,
            HttpMethod.Post,
            ApiClient.SignInPath,
            remoteAddress,
            [(ForwardedFor, forwardedFor)],
            JsonContent.Create(new
            {
                email = $"nobody-{Guid.NewGuid():N}@fieldservice.example",
                password = TestData.Password,
                client = nameof(SessionClient.Web),
            }));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var failed = Assert.Single(factory.LogSink.Named(SecurityLog.SignInFailedEvent));
        return CapturingLogSink.Scalar(failed, "ClientIp");
    }
}
