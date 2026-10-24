using FieldService.Api.Features.Auth;
using FieldService.Api.Features.ChecklistTemplates;
using FieldService.Api.Features.Dashboard;
using FieldService.Api.Features.Diagnostics;
using FieldService.Api.Features.Inspections;
using FieldService.Api.Features.Jobs;
using FieldService.Api.Features.Technicians;

namespace FieldService.Api.Features;

public static class FeatureEndpoints
{
    public static IEndpointRouteBuilder MapFeatureEndpoints(this IEndpointRouteBuilder endpoints)
    {
        AuthEndpoints.Map(endpoints);
        DashboardEndpoints.Map(endpoints);
        TechniciansEndpoints.Map(endpoints);
        JobsEndpoints.Map(endpoints);
        InspectionsEndpoints.Map(endpoints);
        ChecklistTemplatesEndpoints.Map(endpoints);
        DiagnosticsEndpoints.Map(endpoints);

        return endpoints;
    }
}
