using FieldService.Api.Domain;
using FieldService.Api.Persistence;
using FieldService.Api.Shared.Errors;
using FieldService.Api.Shared.Http;
using FieldService.Api.Shared.Observability;
using FieldService.Api.Shared.Realtime;
using FieldService.Api.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Features.Jobs.UpdateJobStatus;

public static class UpdateJobStatusEndpoint
{
    public static void Map(RouteGroupBuilder jobs) =>
        jobs.MapPut("/{jobId:guid}/status", HandleAsync)
            .WithName("UpdateJobStatus")
            .WithSummary("Update the status of a job")
            .WithDescription("Starts, puts on hold or resumes a job assigned to the calling technician, and adds a Started, PutOnHold or Resumed history entry. The status must be InProgress or OnHold. Putting a job on hold needs a note of at most 500 characters; a note sent with InProgress is not stored. Send the version you last read; sending the job's current status returns the job unchanged. A technician can change only the jobs assigned to them; any other job returns 404. The response carries the job's details and its new version.")
            .RequireAuthorization(Policies.Technician)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    public static async Task<Results<Ok<JobDetailsResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        Guid jobId,
        UpdateJobStatusRequest request,
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

        var target = request.Status.GetValueOrDefault();
        if (job.Status == target)
        {
            return TypedResults.Ok(await ReadAsync(db, job.Id, actor, cancellationToken));
        }

        var previousStatus = job.Status;
        db.Entry(job).Property(entry => entry.Version).OriginalValue = request.Version.GetValueOrDefault();
        if (job.ChangeStatus(target, request.Note, actor, time.GetUtcNow()) is { } error)
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
            ?? throw new InvalidOperationException("The job could not be read back after its status update.");
}
