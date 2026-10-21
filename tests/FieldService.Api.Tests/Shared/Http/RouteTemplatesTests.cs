using FieldService.Api.Shared.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace FieldService.Api.Tests.Shared.Http;

public sealed class RouteTemplatesTests
{
    [Theory]
    [InlineData("/api/jobs/", "/api/jobs")]
    [InlineData("/api/jobs", "/api/jobs")]
    [InlineData("/api/jobs/{jobId:guid}", "/api/jobs/{jobId:guid}")]
    [InlineData("/", "/")]
    [InlineData("", "/")]
    public void TheRouteTemplateDropsATrailingSlashAndKeepsTheRoot(string rawText, string expected)
    {
        var endpoint = new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(rawText),
            0,
            EndpointMetadataCollection.Empty,
            displayName: null);

        Assert.Equal(expected, RouteTemplates.Of(endpoint));
    }

    [Fact]
    public void NoMatchedEndpointHasNoRouteTemplate()
    {
        Assert.Null(RouteTemplates.Of(null));
    }
}
