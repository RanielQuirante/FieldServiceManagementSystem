using System.Net;
using System.Text.Json;
using FieldService.Api.Tests.Contracts;
using FieldService.Api.Tests.Infrastructure;

namespace FieldService.Api.Tests.Security;

[Collection(DatabaseCollection.Name)]
public sealed class EnvironmentExposureTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string DocumentRoutePrefix = "/openapi/";

    private static readonly string[] _swaggerPaths = ["/swagger", "/swagger/", "/swagger/index.html", "/swagger/v1/swagger.json"];

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task OpenApiDocumentIsServedAnonymouslyInDevelopmentAndTesting(string environment)
    {
        await using var factory = Fixture.CreateFactory(factory => EndpointInventoryTests.ConfigureEnvironment(factory, environment));
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(OpenApiSnapshotTests.DocumentPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.StartsWith("3.1.", document.RootElement.GetProperty("openapi").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public async Task OpenApiDocumentIsNotMappedInStagingOrProduction(string environment)
    {
        await using var factory = Fixture.CreateFactory(factory => EndpointInventoryTests.ConfigureEnvironment(factory, environment));
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(OpenApiSnapshotTests.DocumentPath);

        await AssertNotFoundProblemAsync(response);
        Assert.DoesNotContain(
            EndpointInventoryTests.ReadInventory(factory),
            endpoint => endpoint.Route.StartsWith(DocumentRoutePrefix, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public async Task SwaggerUiIsNotServedInAnyEnvironment(string environment)
    {
        await using var factory = Fixture.CreateFactory(factory => EndpointInventoryTests.ConfigureEnvironment(factory, environment));
        using var client = factory.CreateApiClient();
        var answers = new Dictionary<string, HttpStatusCode>(StringComparer.Ordinal);

        foreach (var path in _swaggerPaths)
        {
            using var response = await client.GetAsync(path);
            answers[path] = response.StatusCode;
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                await AssertNotFoundProblemAsync(response);
            }
        }

        Assert.Equal(_swaggerPaths.ToDictionary(path => path, _ => HttpStatusCode.NotFound), answers);
    }

    private static async Task AssertNotFoundProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemDetailsShapeTests.ProblemMediaType, response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("resource.not-found", problem.RootElement.GetProperty("code").GetString());
    }
}
