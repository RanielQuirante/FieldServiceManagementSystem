using System.Net;
using FieldService.Api.Domain;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.Net.Http.Headers;

namespace FieldService.Api.Tests.Security;

[Collection(DatabaseCollection.Name)]
public sealed class CorsTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string AllowedOrigin = ApiFactory.WebOrigin;

    private const string RequestedHeaders = "authorization,content-type,x-correlation-id,x-fieldservice-csrf";

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task APreflightFromTheConfiguredOriginIsAllowedWithCredentialsAndTheContractHeaders(string method)
    {
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await PreflightAsync(client, "/api/jobs", AllowedOrigin, method, RequestedHeaders);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(AllowedOrigin, Single(response, HeaderNames.AccessControlAllowOrigin));
        Assert.Equal("true", Single(response, HeaderNames.AccessControlAllowCredentials));
        Assert.Equal(["GET", "POST", "PUT"], List(response, HeaderNames.AccessControlAllowMethods));
        Assert.Equal(
            ["authorization", "content-type", "x-correlation-id", "x-fieldservice-csrf"],
            List(response, HeaderNames.AccessControlAllowHeaders).Select(name => name.ToLowerInvariant()).Order(StringComparer.Ordinal));
        Assert.Equal("600", Single(response, HeaderNames.AccessControlMaxAge));
    }

    [Theory]
    [InlineData("http://evil.example")]
    [InlineData("http://localhost:5174")]
    [InlineData("https://localhost:5173")]
    [InlineData("http://127.0.0.1:5173")]
    [InlineData("null")]
    public async Task APreflightOrRequestFromAnotherOriginGetsNoAllowOrigin(string origin)
    {
        using var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TestTokens.For(Fixture.Factory, UserRole.Administrator);

        using var preflight = await PreflightAsync(client, "/api/jobs", origin, "POST", RequestedHeaders);
        using var actual = await client.SendAsync(WithOrigin(new HttpRequestMessage(HttpMethod.Get, "/api/checklist-templates"), origin));

        Assert.False(preflight.Headers.Contains(HeaderNames.AccessControlAllowOrigin), $"The preflight from {origin} was allowed.");
        Assert.False(preflight.Headers.Contains(HeaderNames.AccessControlAllowCredentials));
        Assert.Equal(HttpStatusCode.OK, actual.StatusCode);
        Assert.False(actual.Headers.Contains(HeaderNames.AccessControlAllowOrigin), $"The response to {origin} carries an allowed origin.");
    }

    [Fact]
    public async Task AnAllowedRequestExposesOnlyTheCorrelationIdLocationAndRetryAfter()
    {
        using var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TestTokens.For(Fixture.Factory, UserRole.Administrator);

        using var response = await client.SendAsync(WithOrigin(new HttpRequestMessage(HttpMethod.Get, "/api/checklist-templates"), AllowedOrigin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AllowedOrigin, Single(response, HeaderNames.AccessControlAllowOrigin));
        Assert.Equal("true", Single(response, HeaderNames.AccessControlAllowCredentials));
        Assert.Equal(
            ["location", "retry-after", "x-correlation-id"],
            List(response, HeaderNames.AccessControlExposeHeaders).Select(name => name.ToLowerInvariant()).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task APreflightForAMethodOutsideThePolicyIsNotGranted(string method)
    {
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await PreflightAsync(client, $"/api/jobs/{Guid.CreateVersion7()}", AllowedOrigin, method, "authorization");

        Assert.DoesNotContain(method, List(response, HeaderNames.AccessControlAllowMethods));
    }

    [Fact]
    public async Task TheAllowedOriginsComeFromConfigurationOnly()
    {
        const string StagingOrigin = "http://127.0.0.1:8081";
        await using var factory = Fixture.CreateFactory(factory => factory.Settings["Cors:AllowedOrigins:0"] = StagingOrigin);
        using var client = factory.CreateApiClient();

        using var configured = await PreflightAsync(client, "/api/jobs", StagingOrigin, "GET", "authorization");
        using var previous = await PreflightAsync(client, "/api/jobs", AllowedOrigin, "GET", "authorization");

        Assert.Equal(StagingOrigin, Single(configured, HeaderNames.AccessControlAllowOrigin));
        Assert.False(previous.Headers.Contains(HeaderNames.AccessControlAllowOrigin));
    }

    [Fact]
    public async Task NoResponseEverCarriesAWildcardOrigin()
    {
        using var client = Fixture.Factory.CreateApiClient();
        string[] origins = [AllowedOrigin, "http://evil.example", "null", "*"];
        var seen = new List<string>();

        foreach (var origin in origins)
        {
            using var preflight = await PreflightAsync(client, "/api/jobs", origin, "GET", "authorization");
            using var anonymous = await client.SendAsync(WithOrigin(new HttpRequestMessage(HttpMethod.Get, "/api/jobs"), origin));
            using var health = await client.SendAsync(WithOrigin(new HttpRequestMessage(HttpMethod.Get, "/health/live"), origin));
            seen.AddRange(new[] { preflight, anonymous, health }.SelectMany(response => List(response, HeaderNames.AccessControlAllowOrigin)));
        }

        Assert.NotEmpty(seen);
        Assert.DoesNotContain("*", seen);
        Assert.All(seen, origin => Assert.Equal(AllowedOrigin, origin));
    }

    private static Task<HttpResponseMessage> PreflightAsync(ApiClient client, string path, string origin, string method, string headers)
    {
        var request = WithOrigin(new HttpRequestMessage(HttpMethod.Options, path), origin);
        request.Headers.Add(HeaderNames.AccessControlRequestMethod, method);
        request.Headers.Add(HeaderNames.AccessControlRequestHeaders, headers);
        return client.SendAsync(request);
    }

    private static HttpRequestMessage WithOrigin(HttpRequestMessage request, string origin)
    {
        request.Headers.Add(HeaderNames.Origin, origin);
        return request;
    }

    private static string Single(HttpResponseMessage response, string name) => Assert.Single(List(response, name));

    private static List<string> List(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
            ? [.. values.SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))]
            : [];
}
