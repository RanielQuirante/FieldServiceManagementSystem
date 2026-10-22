using FieldService.Api.Domain;
using FieldService.Api.Persistence;
using FieldService.Api.Shared.Errors;
using FieldService.Api.Shared.Http;
using FieldService.Api.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Features.Technicians.GetAssignedJobs;

public static class GetAssignedJobsEndpoint
{
    public const int MaxCompletedItems = 50;

    public static void Map(RouteGroupBuilder technicians) =>
        technicians.MapGet("/me/jobs", HandleAsync)
            .WithName("GetAssignedJobs")
            .WithSummary("List my jobs")
            .WithDescription("Returns the jobs assigned to the calling technician, and never another technician's. The active view, the default, holds Assigned, InProgress and OnHold jobs ordered by scheduled start, then job number. The completed view holds Completed and Cancelled jobs ordered by last update with the newest first, at most 50.")
            .RequireAuthorization(Policies.Technician)
            .ValidateQueryParameters();

    public static async Task<Results<Ok<GetAssignedJobsResponse>, ValidationProblem>> HandleAsync(
        [AsParameters] GetAssignedJobsRequest request,
        HttpContext http,
        CurrentUser user,
        FieldServiceDbContext db,
        CancellationToken cancellationToken)
    {
        var view = request.View ?? GetAssignedJobsRequest.DefaultView;
        var technicianId = user.Id;
        var assigned = db.Jobs.AsNoTracking().Where(job => job.AssignedTechnicianId == technicianId);
        var selected = view switch
        {
            GetAssignedJobsRequest.ActiveView => assigned
                .Where(job => job.Status == JobStatus.Assigned
                    || job.Status == JobStatus.InProgress
                    || job.Status == JobStatus.OnHold)
                .OrderBy(job => job.ScheduledStart)
                .ThenBy(job => job.Number),
            GetAssignedJobsRequest.CompletedView => assigned
                .Where(job => job.Status == JobStatus.Completed || job.Status == JobStatus.Cancelled)
                .OrderByDescending(job => job.UpdatedAt)
                .ThenByDescending(job => job.Id)
                .Take(MaxCompletedItems),
            _ => null,
        };
        if (selected is null)
        {
            return Problems.FieldError(http, GetAssignedJobsRequest.ViewParameter, GetAssignedJobsRequest.ViewMessage);
        }

        var items = await selected
            .Select(job => new AssignedJobResponse(
                job.Id,
                job.Number,
                job.Title,
                job.Priority,
                job.Status,
                job.CustomerName,
                job.SiteAddress,
                job.ScheduledStart,
                job.ScheduledEnd,
                job.StartedAt,
                job.CompletedAt,
                job.CancelledAt,
                job.UpdatedAt,
                job.Version))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new GetAssignedJobsResponse(items));
    }
}
