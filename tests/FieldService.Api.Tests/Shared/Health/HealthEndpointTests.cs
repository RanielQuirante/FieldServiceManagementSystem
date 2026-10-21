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

    [Theory]
    [InlineData(HealthRegistration.LivePath)]
    [InlineData(HealthRegistration.ReadyPath)]
    public async Task HealthEndpointsAnswerHeadWithTheStatusAndNoBodyOnKestrel(string path)
    {
        await using var factory = Fixture.CreateFactory(factory => factory.UseKestrel(0));
        using var client = factory.CreateApiClient();

        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(CancellationToken));
    }

    [Theory]
    [InlineData("POST", HealthRegistration.LivePath)]
    [InlineData("PUT", HealthRegistration.LivePath)]
    [InlineData("DELETE", HealthRegistration.ReadyPath)]
    [InlineData("PATCH", HealthRegistration.ReadyPath)]
    [InlineData("OPTIONS", HealthRegistration.LivePath)]
    public async Task HealthEndpointsRefuseEveryOtherMethodWith405AndAllowGetAndHead(string method, string path)
    {
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.MethodNotAllowed, "request.method-not-allowed");
        Assert.Equal(["GET", "HEAD"], response.Content.Headers.Allow.Order(StringComparer.Ordinal));
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
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
