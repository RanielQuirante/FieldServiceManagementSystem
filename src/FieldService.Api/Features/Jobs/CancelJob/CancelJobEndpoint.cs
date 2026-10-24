using FieldService.Api.Domain;
using FieldService.Api.Persistence;
using FieldService.Api.Shared.Errors;
using FieldService.Api.Shared.Http;
using FieldService.Api.Shared.Observability;
using FieldService.Api.Shared.Realtime;
using FieldService.Api.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Features.Jobs.CancelJob;

public static class CancelJobEndpoint
{
    public static void Map(RouteGroupBuilder jobs) =>
        jobs.MapPost("/{jobId:guid}/cancellation", HandleAsync)
            .WithName("CancelJob")
            .WithSummary("Cancel a job")
            .WithDescription("Cancels a job that is Unassigned, Assigned, InProgress or OnHold, sets its cancellation time and adds a Cancelled history entry with the reason as its note. The reason is required and holds at most 500 characters. A Completed job returns 409 job.invalid-transition. Send the version you last read; cancelling a job that is already Cancelled returns the job unchanged and the reason is not stored. The job keeps its assigned technician and becomes read-only, and a draft inspection stays unsubmitted and can no longer be changed. The response carries the job's details and its new version.")
            .RequireAuthorization(Policies.Administrator)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    public static async Task<Results<Ok<JobDetailsResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        Guid jobId,
        CancelJobRequest request,
        HttpContext http,
        CurrentUser user,
        FieldServiceDbContext db,
        TimeProvider time,
        IJobEventQueue events,
        CancellationToken cancellationToken)
    {
        var actor = new JobActor(user.Id, user.Role);
        var job = await db.Jobs.VisibleTo(actor).SingleOrDefaultAsync(entry => entry.Id == jobId, cancellationToken);
        if (job is null)
        {
            return Problems.Of(http, ErrorCodes.JobNotFound);
        }

        if (job.Status == JobStatus.Cancelled)
        {
            return TypedResults.Ok(await ReadAsync(db, job.Id, actor, cancellationToken));
        }

        var previousStatus = job.Status;
        db.Entry(job).Property(entry => entry.Version).OriginalValue = request.Version.GetValueOrDefault();
        if (job.Cancel(request.Reason, actor, time.GetUtcNow()) is { } error)
        {
            return error.IsFieldError ? Problems.FieldError(http, error) : Problems.Conflict(http, error);
        }

        await db.SaveChangesAsync(cancellationToken);
        events.Enqueue(JobEvent.StatusChanged(job, previousStatus, CorrelationId.From(http)));

        return TypedResults.Ok(await ReadAsync(db, job.Id, actor, cancellationToken));
    }

    private static async Task<JobDetailsResponse> ReadAsync(
        FieldServiceDbContext db,
        Guid jobId,
        JobActor actor,
        CancellationToken cancellationToken) =>
        await JobDetailsQuery.FindAsync(db, jobId, actor, cancellationToken)
            ?? throw new InvalidOperationException("The cancelled job could not be read back.");
}
