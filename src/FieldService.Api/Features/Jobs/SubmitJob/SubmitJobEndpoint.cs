using FieldService.Api.Domain;
using FieldService.Api.Persistence;
using FieldService.Api.Shared.Errors;
using FieldService.Api.Shared.Http;
using FieldService.Api.Shared.Observability;
using FieldService.Api.Shared.Realtime;
using FieldService.Api.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Features.Jobs.SubmitJob;

public static class SubmitJobEndpoint
{
    public static void Map(RouteGroupBuilder jobs) =>
        jobs.MapPost("/{jobId:guid}/submission", HandleAsync)
            .WithName("SubmitJob")
            .WithSummary("Submit a job")
            .WithDescription("Completes an InProgress job assigned to the calling technician and submits its inspection in one transaction, and adds a Completed history entry. Every checklist item must have a saved answer; otherwise the response is 409 inspection.incomplete. The site safety confirmation must be true, and the technician notes hold at most 2000 characters. A job that is not InProgress returns 409 job.invalid-transition; a job on hold must be resumed first. Send the version you last read; submitting a job that is already Completed returns the job unchanged. The job and its inspection are read-only afterwards. A technician can submit only the jobs assigned to them; any other job returns 404. The response carries the job's details and its new version.")
            .RequireAuthorization(Policies.Technician)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    public static async Task<Results<Ok<JobDetailsResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        Guid jobId,
        SubmitJobRequest request,
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

        if (job.Status == JobStatus.Completed)
        {
            return TypedResults.Ok(await ReadAsync(db, job.Id, actor, cancellationToken));
        }

        var checklist = await db.ChecklistAsync(job.ChecklistTemplateId, cancellationToken);
        var inspection = await db.Inspections
            .Include(entry => entry.Answers)
            .SingleOrDefaultAsync(entry => entry.JobId == job.Id, cancellationToken);
        var previousStatus = job.Status;
        db.Entry(job).Property(entry => entry.Version).OriginalValue = request.Version.GetValueOrDefault();
        if (job.Submit(inspection, checklist, request.TechnicianNotes, actor, time.GetUtcNow()) is { } error)
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
            ?? throw new InvalidOperationException("The job could not be read back after its submission.");
}
