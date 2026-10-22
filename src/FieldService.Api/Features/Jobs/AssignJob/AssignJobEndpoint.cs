using FieldService.Api.Domain;
using FieldService.Api.Persistence;
using FieldService.Api.Shared.Errors;
using FieldService.Api.Shared.Http;
using FieldService.Api.Shared.Observability;
using FieldService.Api.Shared.Realtime;
using FieldService.Api.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Features.Jobs.AssignJob;

public static class AssignJobEndpoint
{
    public static void Map(RouteGroupBuilder jobs) =>
        jobs.MapPut("/{jobId:guid}/assignment", HandleAsync)
            .WithName("AssignJob")
            .WithSummary("Assign a job to a technician")
            .WithDescription("Assigns an unassigned job, or reassigns an assigned job, to an active technician who is on shift, and adds an Assigned history entry. Send the version you last read; assigning the same technician again returns the job unchanged. The response carries the job's details and its new version.")
            .RequireAuthorization(Policies.Administrator)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    public static async Task<Results<Ok<JobDetailsResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        Guid jobId,
        AssignJobRequest request,
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

        var assignee = await AssigneeQuery.FindAsync(db, request.TechnicianId.GetValueOrDefault(), cancellationToken);
        if (assignee is null)
        {
            return Problems.FieldError(http, AssigneeQuery.Field, AssigneeQuery.UnknownMessage);
        }

        if (job.Status == JobStatus.Assigned && job.IsAssignedTo(assignee.TechnicianId))
        {
            return TypedResults.Ok(await ReadAsync(db, job.Id, actor, cancellationToken));
        }

        var previousStatus = job.Status;
        db.Entry(job).Property(entry => entry.Version).OriginalValue = request.Version.GetValueOrDefault();
        if (job.AssignTo(assignee, actor, time.GetUtcNow()) is { } error)
        {
            return error.IsFieldError ? Problems.FieldError(http, error) : Problems.Conflict(http, error);
        }

        await db.SaveChangesAsync(cancellationToken);
        events.Enqueue(JobEvent.Assigned(job, previousStatus, CorrelationId.From(http)));

        return TypedResults.Ok(await ReadAsync(db, job.Id, actor, cancellationToken));
    }

    private static async Task<JobDetailsResponse> ReadAsync(
        FieldServiceDbContext db,
        Guid jobId,
        JobActor actor,
        CancellationToken cancellationToken) =>
        await JobDetailsQuery.FindAsync(db, jobId, actor, cancellationToken)
            ?? throw new InvalidOperationException("The assigned job could not be read back.");
}
