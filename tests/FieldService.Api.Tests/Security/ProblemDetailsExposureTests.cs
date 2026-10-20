using System.Net;
using System.Text.Json;
using FieldService.Api.Shared.Observability;
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

    private static void Configure(ApiFactory factory, string environment, ProblemDetailsShapeTests.FailureProbe probe)
    {
        EndpointInventoryTests.ConfigureEnvironment(factory, environment);
        probe.Install(factory);
    }
}
