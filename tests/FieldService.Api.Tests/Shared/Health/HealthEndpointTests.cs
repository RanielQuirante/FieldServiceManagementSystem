using System.Net;
using FieldService.Api.Persistence;
using FieldService.Api.Shared.Health;
using FieldService.Api.Tests.Infrastructure;

namespace FieldService.Api.Tests.Shared.Health;

[Collection(DatabaseCollection.Name)]
public sealed class HealthEndpointTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string UnreachableDatabase =
        "Host=127.0.0.1;Port=1;Database=fieldservice;Username=fieldservice_app;Timeout=2;Pooling=false";

    [Fact]
    public async Task LiveEndpointAnswersHealthyToAnAnonymousCaller()
    {
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.GetAsync(HealthRegistration.LivePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(CancellationToken));
    }

    [Fact]
    public async Task ReadyEndpointAnswersHealthyWhenTheDatabaseIsReachable()
    {
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.GetAsync(HealthRegistration.ReadyPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(CancellationToken));
    }

    [Fact]
    public async Task ReadyEndpointAnswersUnhealthyWhenTheDatabaseIsUnreachable()
    {
        await using var factory = Fixture.CreateFactory(factory =>
            factory.Settings[$"ConnectionStrings:{PersistenceRegistration.ConnectionStringName}"] = UnreachableDatabase);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(HealthRegistration.ReadyPath);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync(CancellationToken));
    }

    [Fact]
    public async Task LiveEndpointStaysHealthyWhenTheDatabaseIsUnreachable()
    {
        await using var factory = Fixture.CreateFactory(factory =>
            factory.Settings[$"ConnectionStrings:{PersistenceRegistration.ConnectionStringName}"] = UnreachableDatabase);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(HealthRegistration.LivePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(CancellationToken));
    }
}
