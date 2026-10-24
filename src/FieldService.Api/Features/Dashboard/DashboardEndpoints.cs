using FieldService.Api.Features.Dashboard.GetDashboardSummary;
using FieldService.Api.Shared.Security;

namespace FieldService.Api.Features.Dashboard;

public static class DashboardEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var dashboard = app.MapGroup("/api/dashboard")
            .WithTags("Dashboard")
            .RequireAuthorization(Policies.Administrator);
        dashboard.ProducesProblem(StatusCodes.Status401Unauthorized);
        dashboard.ProducesProblem(StatusCodes.Status403Forbidden);
        dashboard.ProducesProblem(StatusCodes.Status429TooManyRequests);

        GetDashboardSummaryEndpoint.Map(dashboard);
    }
}
