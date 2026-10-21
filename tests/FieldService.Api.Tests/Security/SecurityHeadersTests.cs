using System.Net;
using System.Text;
using FieldService.Api.Domain;
using FieldService.Api.Features.Auth;
using FieldService.Api.Shared.Observability;
using FieldService.Api.Tests.Contracts;
using FieldService.Api.Tests.Features.Auth;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;

namespace FieldService.Api.Tests.Security;

[Collection(DatabaseCollection.Name)]
public sealed class SecurityHeadersTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    public const string DenyAllPolicy = "default-src 'none'; frame-ancestors 'none'";

    private const string KnownProxy = "172.30.80.10";

    private const string PublicHost = "fieldservice.example";

    public static TheoryData<string> ApiKinds =>
    [
        "GET /api/checklist-templates 200",
        "POST /api/auth/sign-in 200",
        "POST /api/jobs 201",
        "POST /api/auth/sign-out 204",
        "GET /api/jobs?page=0 400",
        "GET /api/jobs 401",
        "GET /api/jobs 403",
        "GET /api/jobs/{unknown} 404",
        "GET /api/no-such-route 404",
        "DELETE /api/jobs/{id} 405",
        "POST /api/auth/sign-in 413",
        "POST /api/auth/sign-in 415",
        "POST /api/auth/sign-in 429",
        "GET /api/problem-probe 500",
    ];

    public static TheoryData<string> OtherKinds =>
    [
        "GET /health/live 200",
        "HEAD /health/live 200",
        "GET /health/ready 200",
        "POST /health/live 405",
        "GET /openapi/v1.json 200",
        "GET /favicon.ico 404",
        "GET /swagger 404",
    ];

    [Theory]
    [MemberData(nameof(ApiKinds))]
    [MemberData(nameof(OtherKinds))]
    public async Task EveryResponseCarriesNosniffNoReferrerAndACorrelationIdAndNoServerHeader(string kind)
    {
        var facts = await ObserveAsync(Fixture, kind);

        Assert.Equal(ExpectedStatus(kind), facts.Status);
        Assert.Equal("nosniff", facts.ContentTypeOptions);
        Assert.Equal("no-referrer", facts.ReferrerPolicy);
        Assert.False(string.IsNullOrEmpty(facts.CorrelationHeader), $"{kind}: X-Correlation-ID is missing.");
        Assert.Null(facts.Server);
    }

    [Theory]
    [MemberData(nameof(ApiKinds))]
    [MemberData(nameof(OtherKinds))]
    public async Task TheDenyAllPolicyIsOnApiAndHealthResponsesOnly(string kind)
    {
        var facts = await ObserveAsync(Fixture, kind);

        var path = kind.Split(' ')[1];
        var expected = path.StartsWith("/api/", StringComparison.Ordinal) || path.StartsWith("/health/", StringComparison.Ordinal)
            ? DenyAllPolicy
            : null;
        Assert.Equal(expected, facts.ContentSecurityPolicy);
    }

    [Fact]
    public async Task KestrelSendsNoServerHeaderOnSuccessErrorsOrRejectedBodies()
    {
        await using var factory = Fixture.CreateFactory(factory => factory.UseKestrel(0));
        using var client = factory.CreateApiClient();

        using var health = await client.GetAsync("/health/live");
        using var unauthenticated = await client.GetAsync("/api/jobs");
        using var tooLarge = await client.PostAsync(
            ApiClient.SignInPath,
            SignInTests.SignInBodyOfLength((int)AuthEndpoints.RequestBodyLimit + 1));

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.All(new[] { health, unauthenticated, tooLarge }, response =>
        {
            Assert.False(response.Headers.Contains(HeaderNames.Server), "A Server header was sent.");
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues(HeaderNames.XContentTypeOptions)));
        });
    }

    [Theory]
    [InlineData("Staging", KnownProxy, "https", PublicHost, true)]
    [InlineData("Production", KnownProxy, "https", PublicHost, true)]
    [InlineData("Staging", "172.30.80.11", "https", PublicHost, false)]
    [InlineData("Staging", "127.0.0.1", "https", PublicHost, false)]
    [InlineData("Staging", KnownProxy, null, PublicHost, false)]
    [InlineData("Staging", KnownProxy, "https", "localhost", false)]
    [InlineData("Development", "127.0.0.1", "https", "10.0.2.2", false)]
    public async Task HstsIsSentOnlyForHttpsFromATrustedProxyOutsideDevelopmentWithANonLoopbackHost(
        string environment,
        string remoteAddress,
        string? forwardedProto,
        string host,
        bool expected)
    {
        await using var factory = Fixture.CreateFactory(factory =>
        {
            EndpointInventoryTests.ConfigureEnvironment(factory, environment);
            if (environment != "Development")
            {
                factory.Settings["ForwardedHeaders:KnownProxies:0"] = KnownProxy;
                factory.Settings["AllowedHosts"] = $"{PublicHost};localhost";
            }
        });
        using var http = ClientFor(factory, $"http://{host}");

        using var response = await ForwardedHeadersTests.SendFromAsync(
            http,
            HttpMethod.Get,
            "/health/live",
            remoteAddress,
            forwardedProto is null ? [] : [("X-Forwarded-Proto", forwardedProto)]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (expected)
        {
            Assert.Equal("max-age=31536000", Assert.Single(response.Headers.GetValues(HeaderNames.StrictTransportSecurity)));
        }
        else
        {
            Assert.False(response.Headers.Contains(HeaderNames.StrictTransportSecurity), "An HSTS header was sent.");
        }
    }

    public static HttpClient ClientFor(ApiFactory factory, string baseAddress) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(baseAddress),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    public static int ExpectedStatus(string kind) => int.Parse(kind.Split(' ')[^1], System.Globalization.CultureInfo.InvariantCulture);

    public static async Task<ResponseFacts> ObserveAsync(DatabaseFixture fixture, string kind)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var factory = fixture.Factory;
        var data = new TestData(fixture, factory.Clock.GetUtcNow());
        switch (kind)
        {
            case "POST /api/auth/sign-in 413":
                await using (var kestrel = fixture.CreateFactory(created => created.UseKestrel(0)))
                {
                    using var kestrelClient = kestrel.CreateApiClient();
                    using var response = await kestrelClient.PostAsync(
                        ApiClient.SignInPath,
                        SignInTests.SignInBodyOfLength((int)AuthEndpoints.RequestBodyLimit + 1));
                    return ResponseFacts.Of(response);
                }

            case "GET /api/problem-probe 500":
                var probe = new ProblemDetailsShapeTests.FailureProbe();
                await using (var failing = fixture.CreateFactory(probe.Install))
                {
                    using var failingClient = failing.CreateApiClient();
                    using var response = await failingClient.GetAsync(probe.RequestUri);
                    return ResponseFacts.Of(response);
                }
        }

        using var client = factory.CreateApiClient();
        switch (kind)
        {
            case "GET /api/checklist-templates 200":
                client.AccessToken = TestTokens.For(factory, UserRole.Administrator);
                return await ObserveAsync(client.GetAsync("/api/checklist-templates"));
            case "POST /api/auth/sign-in 200":
                var administrator = await data.CreateAdministratorAsync(cancellationToken);
                return await ObserveAsync(client.SignInAsync(administrator.Email, TestData.Password, SessionClient.Web));
            case "POST /api/jobs 201":
                var creator = await data.CreateAdministratorAsync(cancellationToken);
                client.AccessToken = TestTokens.For(factory, creator);
                return await ObserveAsync(client.PostJsonAsync("/api/jobs", RoleMatrixTests.CreateJobBody(data.Now, "headers")));
            case "POST /api/auth/sign-out 204":
                return await ObserveAsync(client.PostAsync(ApiClient.SignOutPath));
            case "GET /api/jobs?page=0 400":
                client.AccessToken = TestTokens.For(factory, UserRole.Administrator);
                return await ObserveAsync(client.GetAsync("/api/jobs?page=0"));
            case "GET /api/jobs 401":
                return await ObserveAsync(client.GetAsync("/api/jobs"));
            case "GET /api/jobs 403":
                client.AccessToken = TestTokens.For(factory, UserRole.Technician);
                return await ObserveAsync(client.GetAsync("/api/jobs"));
            case "GET /api/jobs/{unknown} 404":
                client.AccessToken = TestTokens.For(factory, UserRole.Administrator);
                return await ObserveAsync(client.GetAsync($"/api/jobs/{Guid.CreateVersion7()}"));
            case "GET /api/no-such-route 404":
                return await ObserveAsync(client.GetAsync("/api/no-such-route"));
            case "DELETE /api/jobs/{id} 405":
                client.AccessToken = TestTokens.For(factory, UserRole.Administrator);
                return await ObserveAsync(client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/jobs/{Guid.CreateVersion7()}")));
            case "POST /api/auth/sign-in 415":
                return await ObserveAsync(client.PostAsync(ApiClient.SignInPath, new StringContent("email=a", Encoding.UTF8, "text/plain")));
            case "POST /api/auth/sign-in 429":
                for (var attempt = 1; attempt <= 10; attempt++)
                {
                    using var allowed = await client.PostJsonAsync(ApiClient.SignInPath, new { });
                }

                return await ObserveAsync(client.PostJsonAsync(ApiClient.SignInPath, new { }));
            case "GET /health/live 200":
                return await ObserveAsync(client.GetAsync("/health/live"));
            case "HEAD /health/live 200":
                return await ObserveAsync(client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/health/live")));
            case "GET /health/ready 200":
                return await ObserveAsync(client.GetAsync("/health/ready"));
            case "POST /health/live 405":
                return await ObserveAsync(client.PostAsync("/health/live"));
            case "GET /openapi/v1.json 200":
                return await ObserveAsync(client.GetAsync("/openapi/v1.json"));
            case "GET /favicon.ico 404":
                return await ObserveAsync(client.GetAsync("/favicon.ico"));
            case "GET /swagger 404":
                return await ObserveAsync(client.GetAsync("/swagger"));
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown request kind.");
        }
    }

    private static async Task<ResponseFacts> ObserveAsync(Task<HttpResponseMessage> request)
    {
        using var response = await request;
        return ResponseFacts.Of(response);
    }

    public sealed record ResponseFacts(
        int Status,
        string? CacheControl,
        string? CorrelationHeader,
        string? ContentTypeOptions,
        string? ReferrerPolicy,
        string? ContentSecurityPolicy,
        string? Server,
        string? MediaType)
    {
        public static ResponseFacts Of(HttpResponseMessage response) => new(
            (int)response.StatusCode,
            Header(response, HeaderNames.CacheControl),
            Header(response, CorrelationId.HeaderName),
            Header(response, HeaderNames.XContentTypeOptions),
            Header(response, "Referrer-Policy"),
            Header(response, HeaderNames.ContentSecurityPolicy),
            Header(response, HeaderNames.Server),
            response.Content.Headers.ContentType?.MediaType);

        private static string? Header(HttpResponseMessage response, string name) =>
            response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)
                ? string.Join(", ", values)
                : null;
    }
}
