using FieldService.Api.Domain;
using FieldService.Api.Persistence;
using FieldService.Api.Shared.Errors;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FieldService.Api.Features.Dashboard.GetDashboardSummary;

public static class GetDashboardSummaryEndpoint
{
    public static void Map(RouteGroupBuilder dashboard) =>
        dashboard.MapGet("/summary", HandleAsync)
            .WithName("GetDashboardSummary")
            .WithSummary("Get the dashboard summary")
            .WithDescription("Returns the dashboard's figures in one read. The open job counts describe the current state, independent of the window. The range counts the jobs created, completed and cancelled inside the window, which starts at from (inclusive) and ends at to (exclusive). Both are required instants with Z or a numeric offset; to must be after from, and the window may span at most 31 days. The technician counts include active technicians only. Needs a technician lists at most the 10 earliest-scheduled Unassigned jobs, and recent activity lists at most the 6 newest history entries inside the window, newest first.")
            .ValidateQueryParameters();

    public static async Task<Results<Ok<DashboardSummaryResponse>, ValidationProblem>> HandleAsync(
        [AsParameters] GetDashboardSummaryRequest request,
        FieldServiceDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var generatedAt = UtcInstant.Truncate(time.GetUtcNow());
        var from = UtcInstant.Truncate(request.From.GetValueOrDefault());
        var to = UtcInstant.Truncate(request.To.GetValueOrDefault());

        return TypedResults.Ok(await DashboardSummaryQuery.ReadAsync(db, generatedAt, from, to, cancellationToken));
    }
}
