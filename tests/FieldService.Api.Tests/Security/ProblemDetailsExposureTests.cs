using System.Net;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Observability;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Contracts;
using FieldService.Api.Tests.Infrastructure;

namespace FieldService.Api.Tests.Security;

[Collection(DatabaseCollection.Name)]
public sealed class ProblemDetailsExposureTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    [Theory]
    [InlineData("Testing")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public async Task UnhandledFailureProblemCarriesNoExceptionDetailOutsideDevelopment(string environment)
    {
        var probe = new ProblemDetailsShapeTests.FailureProbe();
        await using var factory = Fixture.CreateFactory(factory => Configure(factory, environment, probe));
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(probe.RequestUri);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ProblemDetailsShapeTests.ProblemMediaType, response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync(CancellationToken);
        using var body = JsonDocument.Parse(text);
        var problem = body.RootElement;
        Assert.Equal("server.error", problem.GetProperty("code").GetString());
        Assert.Equal(client.LastCorrelationId, problem.GetProperty("correlationId").GetString());
        Assert.Equal(client.LastCorrelationId, Assert.Single(response.Headers.GetValues(CorrelationId.HeaderName)));
        Assert.Matches(ProblemDetailsShapeTests.TraceParentPattern(), problem.GetProperty("traceId").GetString());
        Assert.Equal(ProblemDetailsShapeTests.FailureProbe.Path, problem.GetProperty("instance").GetString());
        probe.AssertNoExceptionDetail(text);
    }

    [Fact]
    public async Task DevelopmentProblemCarriesTheExceptionMessageInDetail()
    {
        var probe = new ProblemDetailsShapeTests.FailureProbe();
        await using var factory = Fixture.CreateFactory(factory => Configure(factory, "Development", probe));
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(probe.RequestUri);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(CancellationToken);
        using var body = JsonDocument.Parse(text);
        var problem = body.RootElement;
        Assert.Equal(probe.ExceptionMessage, problem.GetProperty("detail").GetString());
        Assert.Equal("server.error", problem.GetProperty("code").GetString());
        Assert.Equal(client.LastCorrelationId, problem.GetProperty("correlationId").GetString());
        Assert.Matches(ProblemDetailsShapeTests.TraceParentPattern(), problem.GetProperty("traceId").GetString());
        Assert.Equal(ProblemDetailsShapeTests.FailureProbe.Path, problem.GetProperty("instance").GetString());
        Assert.DoesNotContain(probe.QueryMarker, text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Testing")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public async Task ClientErrorProblemsCarryNoStackTraceAndAnInstanceWithoutTheQueryString(string environment)
    {
        await using var factory = Fixture.CreateFactory(factory => EndpointInventoryTests.ConfigureEnvironment(factory, environment));
        var marker = $"query-{Guid.NewGuid():N}";
        using var anonymous = factory.CreateApiClient();
        using var technician = factory.CreateApiClient();
        technician.AccessToken = TestTokens.For(factory, UserRole.Technician);
        using var administrator = factory.CreateApiClient();
        administrator.AccessToken = TestTokens.For(factory, UserRole.Administrator);
        var unknownJob = $"/api/jobs/{Guid.CreateVersion7()}";
        var requests = new List<(string Path, HttpResponseMessage Response)>
        {
            ("/api/jobs", await anonymous.GetAsync($"/api/jobs?probe={marker}")),
            ("/api/jobs", await technician.GetAsync($"/api/jobs?probe={marker}")),
            ("/api/jobs", await administrator.GetAsync($"/api/jobs?page=0&probe={marker}")),
            (unknownJob, await administrator.GetAsync($"{unknownJob}?probe={marker}")),
            ("/api/jobs/not-a-guid", await administrator.GetAsync($"/api/jobs/not-a-guid?probe={marker}")),
            (unknownJob, await administrator.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{unknownJob}?probe={marker}"))),
            ("/api/jobs", await administrator.PostAsync($"/api/jobs?probe={marker}", new StringContent("{", System.Text.Encoding.UTF8, "application/json"))),
            ("/api/jobs", await administrator.PostAsync($"/api/jobs?probe={marker}", new StringContent("{}", System.Text.Encoding.UTF8, "text/plain"))),
            (ApiClient.RefreshPath, await anonymous.RefreshWithCookieAsync(RefreshTokenFactory.Create().Value, csrfHeader: false)),
        };

        try
        {
            foreach (var (path, response) in requests)
            {
                Assert.True((int)response.StatusCode is >= 400 and < 500, $"{path} returned {(int)response.StatusCode}.");
                Assert.Equal(ProblemDetailsShapeTests.ProblemMediaType, response.Content.Headers.ContentType?.MediaType);
                var text = await response.Content.ReadAsStringAsync(CancellationToken);
                using var body = JsonDocument.Parse(text);
                Assert.Equal(path, body.RootElement.GetProperty("instance").GetString());
                Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("correlationId").GetString()));
                Assert.Matches(ProblemDetailsShapeTests.TraceParentPattern(), body.RootElement.GetProperty("traceId").GetString());
                Assert.DoesNotContain(marker, text, StringComparison.Ordinal);
                Assert.DoesNotContain("Exception", text, StringComparison.Ordinal);
                Assert.DoesNotContain("   at ", text, StringComparison.Ordinal);
                Assert.DoesNotContain("System.", text, StringComparison.Ordinal);
                Assert.DoesNotContain("Microsoft.", text, StringComparison.Ordinal);
            }
        }
        finally
        {
            foreach (var (_, response) in requests)
            {
                response.Dispose();
            }
        }
    }

    private static void Configure(ApiFactory factory, string environment, ProblemDetailsShapeTests.FailureProbe probe)
    {
        EndpointInventoryTests.ConfigureEnvironment(factory, environment);
        probe.Install(factory);
    }
}
