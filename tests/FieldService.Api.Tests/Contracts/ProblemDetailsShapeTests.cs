using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using FieldService.Api.Shared.Observability;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace FieldService.Api.Tests.Contracts;

[Collection(DatabaseCollection.Name)]
public sealed partial class ProblemDetailsShapeTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    public const string ProblemMediaType = "application/problem+json";

    private static readonly string[] _serverErrorMembers =
        ["type", "title", "status", "instance", "code", "correlationId", "traceId"];

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
