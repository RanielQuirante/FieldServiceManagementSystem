using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Observability;
using FieldService.Api.Tests.Infrastructure;
using FieldService.Api.Tests.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace FieldService.Api.Tests.Contracts;

[Collection(DatabaseCollection.Name)]
public sealed partial class ProblemDetailsShapeTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    public const string ProblemMediaType = "application/problem+json";

    private const string TypePrefix = "https://tools.ietf.org/html/";

    private static readonly string[] _serverErrorMembers =
        ["type", "title", "status", "instance", "code", "correlationId", "traceId"];

    private static readonly string[] _optionalMembers = ["detail", "errors"];

    private static readonly Dictionary<string, (int Status, string Type, string Title)> _registry = new(StringComparer.Ordinal)
    {
        ["request.validation-failed"] = (400, "rfc9110#section-15.5.1", "One or more fields are invalid."),
        ["auth.unauthenticated"] = (401, "rfc9110#section-15.5.2", "Authentication is required."),
        ["auth.invalid-credentials"] = (401, "rfc9110#section-15.5.2", "The email or password is incorrect."),
        ["auth.forbidden"] = (403, "rfc9110#section-15.5.4", "This action is not allowed for your role."),
        ["auth.wrong-application"] = (403, "rfc9110#section-15.5.4", "This account uses the other FieldService app."),
        ["auth.csrf-required"] = (403, "rfc9110#section-15.5.4", "The request is missing the anti-forgery header."),
        ["job.not-found"] = (404, "rfc9110#section-15.5.5", "The job was not found."),
        ["resource.not-found"] = (404, "rfc9110#section-15.5.5", "The resource was not found."),
        ["request.method-not-allowed"] = (405, "rfc9110#section-15.5.6", "The method is not allowed for this resource."),
        ["request.too-large"] = (413, "rfc9110#section-15.5.14", "The request body is too large."),
        ["request.unsupported-media-type"] = (415, "rfc9110#section-15.5.16", "The request body must be JSON."),
        ["rate-limit.exceeded"] = (429, "rfc6585#section-4", "Too many requests."),
    };

    public static TheoryData<string, string, string> ClientErrors => new()
    {
        { "CreateJob without a title", "request.validation-failed", "title" },
        { "CreateJob with priority Critical", "request.validation-failed", "priority" },
        { "CreateJob with a body that is not JSON", "request.validation-failed", "body" },
        { "CreateJob without a site address", "request.validation-failed", "siteAddress" },
        { "CreateJob with the end not after the start", "request.validation-failed", "scheduledEnd" },
        { "CreateJob without a site address and with the end not after the start", "request.validation-failed", "scheduledEnd,siteAddress" },
        { "CreateJob without a title and a customer", "request.validation-failed", "customerName,title" },
        { "GetJobs with page 0", "request.validation-failed", "page" },
        { "GetJobs without a token", "auth.unauthenticated", "" },
        { "SignIn with a wrong password", "auth.invalid-credentials", "" },
        { "GetJobs as a technician", "auth.forbidden", "" },
        { "SignIn with the wrong client", "auth.wrong-application", "" },
        { "a cookie refresh without the CSRF header", "auth.csrf-required", "" },
        { "GetJobById with an unknown id", "job.not-found", "" },
        { "GetJobById on another technician's job", "job.not-found", "" },
        { "a job route whose id is not a GUID", "resource.not-found", "" },
        { "DELETE on a job", "request.method-not-allowed", "" },
        { "a job request of 1 MB + 1", "request.too-large", "" },
        { "CreateJob with a text/plain body", "request.unsupported-media-type", "" },
        { "the eleventh sign-in in a minute", "rate-limit.exceeded", "" },
    };

    [Theory]
    [MemberData(nameof(ClientErrors))]
    public async Task EachClientErrorReturnsTheRegistryProblemShape(string request, string code, string errorKeys)
    {
        var observed = await SendAsync(request);

        using var body = observed.Body;
        var problem = body.RootElement;
        var registry = _registry[code];
        Assert.Equal(registry.Status, observed.Status);
        Assert.Equal(ProblemMediaType, observed.MediaType);
        Assert.Equal("no-store", observed.CacheControl);
        Assert.Equal(observed.SentCorrelationId, observed.CorrelationHeader);
        var members = problem.EnumerateObject().Select(member => member.Name).ToList();
        Assert.All(_serverErrorMembers, required => Assert.Contains(required, members));
        Assert.All(members, member => Assert.Contains(member, _serverErrorMembers.Concat(_optionalMembers)));
        Assert.Equal(TypePrefix + registry.Type, problem.GetProperty("type").GetString());
        Assert.Equal(registry.Title, problem.GetProperty("title").GetString());
        Assert.Equal(registry.Status, problem.GetProperty("status").GetInt32());
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal(observed.Path, problem.GetProperty("instance").GetString());
        Assert.Equal(observed.CorrelationHeader, problem.GetProperty("correlationId").GetString());
        Assert.Matches(TraceParentPattern(), problem.GetProperty("traceId").GetString());
        Assert.DoesNotContain("Exception", observed.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", observed.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(observed.QueryMarker, observed.Text, StringComparison.Ordinal);
        if (errorKeys.Length == 0)
        {
            Assert.False(problem.TryGetProperty("errors", out _), "Only request.validation-failed carries errors.");
        }
        else
        {
            var errors = problem.GetProperty("errors");
            Assert.Equal(
                errorKeys.Split(',').Order(StringComparer.Ordinal),
                errors.EnumerateObject().Select(member => member.Name).Order(StringComparer.Ordinal));
            Assert.All(errors.EnumerateObject(), member =>
            {
                Assert.Equal(JsonValueKind.Array, member.Value.ValueKind);
                Assert.NotEmpty(member.Value.EnumerateArray());
            });
        }

        if (code == "request.method-not-allowed")
        {
            Assert.Equal("GET", observed.Allow);
        }

        if (code == "rate-limit.exceeded")
        {
            Assert.Matches("^[1-9][0-9]*$", observed.RetryAfter);
            Assert.Equal($"Try again in {observed.RetryAfter} seconds.", problem.GetProperty("detail").GetString());
        }
    }

    [Fact]
    public async Task AnotherTechniciansJobAndAMissingJobGiveIdenticalBodiesApartFromTheRequestMembers()
    {
        var foreign = await SendAsync("GetJobById on another technician's job");
        var missing = await SendAsync("GetJobById with an unknown id");

        using var foreignBody = foreign.Body;
        using var missingBody = missing.Body;
        Assert.Equal(WithoutRequestMembers(missingBody), WithoutRequestMembers(foreignBody));
        Assert.NotEqual(foreign.Path, missing.Path);
    }

    [Fact]
    public async Task UnhandledFailureReturnsTheServerErrorProblemShape()
    {
        var probe = new FailureProbe();
        await using var factory = Fixture.CreateFactory(factory => probe.Install(factory));
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(probe.RequestUri);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ProblemMediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore, "Cache-Control: no-store is missing.");
        var correlationId = Assert.Single(response.Headers.GetValues(CorrelationId.HeaderName));
        Assert.Equal(client.LastCorrelationId, correlationId);
        var text = await response.Content.ReadAsStringAsync(CancellationToken);
        using var body = JsonDocument.Parse(text);
        var problem = body.RootElement;
        Assert.Equal(
            _serverErrorMembers.Order(StringComparer.Ordinal),
            problem.EnumerateObject().Select(member => member.Name).Where(name => name != "detail").Order(StringComparer.Ordinal));
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.6.1", problem.GetProperty("type").GetString());
        Assert.Equal("An unexpected error occurred.", problem.GetProperty("title").GetString());
        Assert.Equal(500, problem.GetProperty("status").GetInt32());
        Assert.Equal(FailureProbe.Path, problem.GetProperty("instance").GetString());
        Assert.Equal("server.error", problem.GetProperty("code").GetString());
        Assert.Equal(correlationId, problem.GetProperty("correlationId").GetString());
        Assert.Matches(TraceParentPattern(), problem.GetProperty("traceId").GetString());
        probe.AssertNoExceptionDetail(text);
    }

    [GeneratedRegex("^00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$")]
    public static partial Regex TraceParentPattern();

    private static string WithoutRequestMembers(JsonDocument problem)
    {
        var node = JsonNode.Parse(problem.RootElement.GetRawText())!.AsObject();
        node.Remove("instance");
        node.Remove("traceId");
        node.Remove("correlationId");
        return node.ToJsonString();
    }

    private static StringContent JobBody(DateTimeOffset now, Action<JsonObject> change)
    {
        var body = JsonSerializer.SerializeToNode(RoleMatrixTests.CreateJobBody(now, "shape"), ApiClient.Json)!.AsObject();
        change(body);
        return new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
    }

    private async Task<ObservedProblem> SendAsync(string request)
    {
        var marker = $"query-{Guid.NewGuid():N}";
        var data = new TestData(Fixture, Fixture.Factory.Clock.GetUtcNow());
        var now = data.Now;
        if (request == "a job request of 1 MB + 1")
        {
            await using var kestrel = Fixture.CreateFactory(factory => factory.UseKestrel(0));
            using var kestrelClient = kestrel.CreateApiClient();
            kestrelClient.AccessToken = TestTokens.For(kestrel, UserRole.Administrator);
            var oversized = new StringContent($$"""{"title":"{{new string('a', 1024 * 1024)}}"}""", Encoding.UTF8, "application/json");
            using var tooLarge = await kestrelClient.PostAsync("/api/jobs", oversized);
            return await ObservedProblem.ReadAsync(tooLarge, kestrelClient, "/api/jobs", marker);
        }

        using var client = Fixture.Factory.CreateApiClient();
        var administratorToken = TestTokens.For(Fixture.Factory, UserRole.Administrator);
        string path;
        HttpResponseMessage response;
        switch (request)
        {
            case "CreateJob without a title":
                client.AccessToken = administratorToken;
                path = "/api/jobs";
                response = await client.PostAsync(path, JobBody(now, body => body.Remove("title")));
                break;
            case "CreateJob with priority Critical":
                client.AccessToken = administratorToken;
                path = "/api/jobs";
                response = await client.PostAsync(path, JobBody(now, body => body["priority"] = "Critical"));
                break;
            case "CreateJob with a body that is not JSON":
                client.AccessToken = administratorToken;
                path = "/api/jobs";
                response = await client.PostAsync(path, new StringContent("title=Exterior signage", Encoding.UTF8, "application/json"));
                break;
            case "CreateJob without a site address":
                client.AccessToken = administratorToken;
                path = "/api/jobs";
                response = await client.PostAsync(path, JobBody(now, body => body.Remove("siteAddress")));
                break;
            case "CreateJob with the end not after the start":
                client.AccessToken = administratorToken;
                path = "/api/jobs";
                response = await client.PostAsync(path, JobBody(now, body => body["scheduledEnd"] = body["scheduledStart"]!.DeepClone()));
                break;
            case "CreateJob without a site address and with the end not after the start":
                client.AccessToken = administratorToken;
                path = "/api/jobs";
                response = await client.PostAsync(path, JobBody(now, body =>
                {
                    body.Remove("siteAddress");
                    body["scheduledEnd"] = body["scheduledStart"]!.DeepClone();
                }));
                break;
            case "CreateJob without a title and a customer":
                client.AccessToken = administratorToken;
                path = "/api/jobs";
                response = await client.PostAsync(path, JobBody(now, body =>
                {
                    body.Remove("title");
                    body.Remove("customerName");
                }));
                break;
            case "GetJobs with page 0":
                client.AccessToken = administratorToken;
                path = "/api/jobs";
                response = await client.GetAsync($"{path}?page=0&probe={marker}");
                break;
            case "GetJobs without a token":
                path = "/api/jobs";
                response = await client.GetAsync($"{path}?probe={marker}");
                break;
            case "SignIn with a wrong password":
                var signInUser = await data.CreateAdministratorAsync(CancellationToken);
                path = ApiClient.SignInPath;
                response = await client.SignInAsync(signInUser.Email, $"wrong-{marker}", SessionClient.Web);
                break;
            case "GetJobs as a technician":
                client.AccessToken = TestTokens.For(Fixture.Factory, UserRole.Technician);
                path = "/api/jobs";
                response = await client.GetAsync($"{path}?probe={marker}");
                break;
            case "SignIn with the wrong client":
                var technician = await data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
                path = ApiClient.SignInPath;
                response = await client.SignInAsync(technician.User.Email, TestData.Password, SessionClient.Web);
                break;
            case "a cookie refresh without the CSRF header":
                path = ApiClient.RefreshPath;
                response = await client.RefreshWithCookieAsync(RefreshTokenOf43Characters(), csrfHeader: false);
                break;
            case "GetJobById with an unknown id":
                client.AccessToken = TestTokens.For(Fixture.Factory, UserRole.Technician);
                path = $"/api/jobs/{Guid.CreateVersion7()}";
                response = await client.GetAsync($"{path}?probe={marker}");
                break;
            case "GetJobById on another technician's job":
                var world = await RoleMatrixTests.ArrangeAsync(Fixture);
                client.AccessToken = TestTokens.For(Fixture.Factory, world.OtherTechnician);
                path = $"/api/jobs/{world.JobId}";
                response = await client.GetAsync($"{path}?probe={marker}");
                break;
            case "a job route whose id is not a GUID":
                client.AccessToken = administratorToken;
                path = "/api/jobs/JOB-1042";
                response = await client.GetAsync($"{path}?probe={marker}");
                break;
            case "DELETE on a job":
                client.AccessToken = administratorToken;
                path = $"/api/jobs/{Guid.CreateVersion7()}";
                response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{path}?probe={marker}"));
                break;
            case "CreateJob with a text/plain body":
                client.AccessToken = administratorToken;
                path = "/api/jobs";
                response = await client.PostAsync(path, new StringContent("{}", Encoding.UTF8, "text/plain"));
                break;
            case "the eleventh sign-in in a minute":
                path = ApiClient.SignInPath;
                for (var attempt = 1; attempt <= 10; attempt++)
                {
                    using var allowed = await client.PostJsonAsync(path, new { });
                }

                response = await client.PostJsonAsync(path, new { });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request), request, "Unknown problem request.");
        }

        using (response)
        {
            return await ObservedProblem.ReadAsync(response, client, path, marker);
        }
    }

    private static string RefreshTokenOf43Characters() =>
        Convert.ToBase64String(Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_')[..43];

    private sealed record ObservedProblem(
        int Status,
        string? MediaType,
        string? CacheControl,
        string? CorrelationHeader,
        string? SentCorrelationId,
        string? Allow,
        string? RetryAfter,
        string Path,
        string QueryMarker,
        string Text)
    {
        public JsonDocument Body => JsonDocument.Parse(Text);

        public static async Task<ObservedProblem> ReadAsync(HttpResponseMessage response, ApiClient client, string path, string marker)
        {
            var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            return new ObservedProblem(
                (int)response.StatusCode,
                response.Content.Headers.ContentType?.MediaType,
                response.Headers.CacheControl?.ToString(),
                response.Headers.TryGetValues(CorrelationId.HeaderName, out var correlation) ? string.Join(",", correlation) : null,
                client.LastCorrelationId,
                response.Content.Headers.Allow.Count == 0 ? null : string.Join(",", response.Content.Headers.Allow),
                response.Headers.TryGetValues(HeaderNames.RetryAfter, out var retry)
                    ? string.Join(",", retry)
                    : response.Headers.RetryAfter?.Delta?.TotalSeconds.ToString(CultureInfo.InvariantCulture),
                path,
                marker,
                text);
        }
    }

    public sealed class FailureProbe
    {
        public const string Path = "/api/problem-probe/unhandled-failure";

        private readonly string _marker = Guid.NewGuid().ToString("N");

        public string QueryMarker { get; } = $"query-{Guid.NewGuid():N}";

        public string RequestUri => $"{Path}?probe={QueryMarker}";

        public string ExceptionMessage =>
            $"Failure probe {_marker} while running SELECT probe_value FROM fieldservice.probe_rows";

        public void Install(ApiFactory factory)
        {
            factory.TestServices = services => services.AddSingleton<IStartupFilter>(new ThrowingStartupFilter(this));
        }

        public void AssertNoExceptionDetail(string body)
        {
            Assert.DoesNotContain(_marker, body, StringComparison.Ordinal);
            Assert.DoesNotContain("SELECT", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(nameof(FailureProbeException), body, StringComparison.Ordinal);
            Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
            Assert.DoesNotContain(QueryMarker, body, StringComparison.Ordinal);
        }

        private sealed class ThrowingStartupFilter(FailureProbe probe) : IStartupFilter
        {
            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            {
                return app =>
                {
                    next(app);
                    app.Use((context, nextMiddleware) => context.Request.Path == Path
                        ? throw new FailureProbeException(probe.ExceptionMessage)
                        : nextMiddleware(context));
                };
            }
        }
    }

    public sealed class FailureProbeException(string message) : Exception(message);
}
