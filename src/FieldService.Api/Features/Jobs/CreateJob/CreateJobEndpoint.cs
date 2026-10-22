using FieldService.Api.Domain;
using FieldService.Api.Persistence;
using FieldService.Api.Shared.Http;
using FieldService.Api.Shared.Observability;
using FieldService.Api.Shared.Realtime;
using FieldService.Api.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Features.Jobs.CreateJob;

public static class CreateJobEndpoint
{
    public const string UnknownTemplateMessage = "Choose one of the checklist templates.";

    public static void Map(RouteGroupBuilder jobs) =>
        jobs.MapPost(string.Empty, HandleAsync)
            .WithName("CreateJob")
            .WithSummary("Create a job")
            .WithDescription("Creates a job with the next job number and a Created history entry. Without a technician the job starts Unassigned. With a technician, who must be active and on shift, it starts Assigned and also gets an Assigned history entry at the same instant. The response carries the job's details and version, and its Location header points to the job.")
            .RequireAuthorization(Policies.Administrator)
            .ProducesProblem(StatusCodes.Status409Conflict);

    public static async Task<Results<Created<JobDetailsResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        CreateJobRequest request,
        HttpContext http,
        CurrentUser user,
        FieldServiceDbContext db,
        TimeProvider time,
        IJobEventQueue events,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var templateId = request.ChecklistTemplateId.GetValueOrDefault();
        if (!await db.ChecklistTemplates.AnyAsync(template => template.Id == templateId, cancellationToken))
        {
            errors["checklistTemplateId"] = [UnknownTemplateMessage];
        }

        JobAssignee? assignee = null;
        if (request.TechnicianId is { } technicianId)
        {
            assignee = await AssigneeQuery.FindAsync(db, technicianId, cancellationToken);
            if (assignee is null)
            {
                errors[AssigneeQuery.Field] = [AssigneeQuery.UnknownMessage];
            }
        }

        if (errors.Count > 0)
        {
            return Problems.FieldErrors(http, errors);
        }

        var now = time.GetUtcNow();
        var actor = JobActor.Administrator(user.Id);
        var job = Job.Create(
            new JobDetails(
                request.Title,
                request.Description,
                request.Priority.GetValueOrDefault(),
                request.CustomerName,
                request.SiteAddress,
                request.SiteAccessNotes,
                request.ContactName,
                request.ContactPhone,
                request.Equipment,
                request.ScheduledStart.GetValueOrDefault(),
                request.ScheduledEnd.GetValueOrDefault(),
                templateId),
            user.Id,
            now);
        if (assignee is not null && job.AssignTo(assignee, actor, now) is { } error)
        {
            return error.IsFieldError ? Problems.FieldError(http, error) : Problems.Conflict(http, error);
        }

        db.Jobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);

        events.Enqueue(JobEvent.Created(job, CorrelationId.From(http)));

        var details = await JobDetailsQuery.FindAsync(db, job.Id, actor, cancellationToken)
            ?? throw new InvalidOperationException("The created job could not be read back.");
        return TypedResults.Created($"/api/jobs/{job.Id}", details);
    }
}
