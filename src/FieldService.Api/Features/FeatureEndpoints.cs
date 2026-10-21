using FieldService.Api.Features.Auth;
using FieldService.Api.Features.ChecklistTemplates;
using FieldService.Api.Features.Jobs;

namespace FieldService.Api.Features;

public static class FeatureEndpoints
{
    public static IEndpointRouteBuilder MapFeatureEndpoints(this IEndpointRouteBuilder endpoints)
    {
        AuthEndpoints.Map(endpoints);
        JobsEndpoints.Map(endpoints);
        ChecklistTemplatesEndpoints.Map(endpoints);

        return endpoints;
    }
}
