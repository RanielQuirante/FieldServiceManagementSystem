using FieldService.Api.Tests.Infrastructure;

namespace FieldService.Api.Tests.Security;

[Collection(DatabaseCollection.Name)]
public sealed class CacheControlTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    [Theory]
    [MemberData(nameof(SecurityHeadersTests.ApiKinds), MemberType = typeof(SecurityHeadersTests))]
    public async Task EveryApiResponseCarriesNoStoreAndTheCorrelationIdWhateverItsStatus(string kind)
    {
        var facts = await SecurityHeadersTests.ObserveAsync(Fixture, kind);

        Assert.Equal(SecurityHeadersTests.ExpectedStatus(kind), facts.Status);
        Assert.Equal("no-store", facts.CacheControl);
        Assert.False(string.IsNullOrEmpty(facts.CorrelationHeader), $"{kind}: X-Correlation-ID is missing.");
    }

    [Theory]
    [InlineData("GET /health/live 200")]
    [InlineData("GET /health/ready 200")]
    public async Task HealthResponsesKeepTheHealthCheckNoCacheHeaders(string kind)
    {
        var facts = await SecurityHeadersTests.ObserveAsync(Fixture, kind);

        Assert.Equal(200, facts.Status);
        Assert.NotNull(facts.CacheControl);
        Assert.Contains("no-store", facts.CacheControl, StringComparison.Ordinal);
        Assert.Contains("no-cache", facts.CacheControl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheOpenApiDocumentSendsNoCachingHeader()
    {
        var facts = await SecurityHeadersTests.ObserveAsync(Fixture, "GET /openapi/v1.json 200");

        Assert.Equal(200, facts.Status);
        Assert.Null(facts.CacheControl);
    }
}
