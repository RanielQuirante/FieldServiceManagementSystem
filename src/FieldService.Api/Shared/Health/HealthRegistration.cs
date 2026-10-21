using FieldService.Api.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;

namespace FieldService.Api.Shared.Health;

public static class HealthRegistration
{
    public const string LivePath = "/health/live";

    public const string ReadyPath = "/health/ready";

    public static readonly IReadOnlyList<string> Methods = [HttpMethods.Get, HttpMethods.Head];

    private const string DatabaseCheckName = "database";

    private const string ReadyTag = "ready";

    public static IServiceCollection AddHealth(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddDbContextCheck<FieldServiceDbContext>(DatabaseCheckName, tags: [ReadyTag]);

        return services;
    }

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(LivePath, new HealthCheckOptions
        {
            Predicate = static _ => false,
        })
            .WithMetadata(new HttpMethodMetadata(Methods))
            .AllowAnonymous();

        endpoints.MapHealthChecks(ReadyPath, new HealthCheckOptions
        {
            Predicate = static registration => registration.Tags.Contains(ReadyTag),
        })
            .WithMetadata(new HttpMethodMetadata(Methods))
            .AllowAnonymous();

        return endpoints;
    }
}
