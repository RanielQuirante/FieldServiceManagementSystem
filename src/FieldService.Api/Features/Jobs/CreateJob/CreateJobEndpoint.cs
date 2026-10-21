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
            .WithDescription("Creates an unassigned job with the next job number and a Created history entry. The response carries the job's details and version, and its Location header points to the job.")
            .RequireAuthorization(Policies.Administrator);

    public static async Task<Results<Created<JobDetailsResponse>, ValidationProblem>> HandleAsync(
        CreateJobRequest request,
        HttpContext http,
        CurrentUser user,
        FieldServiceDbContext db,
        TimeProvider time,
        IJobEventQueue events,
        CancellationToken cancellationToken)
    {
        var templateId = request.ChecklistTemplateId.GetValueOrDefault();
        if (!await db.ChecklistTemplates.AnyAsync(template => template.Id == templateId, cancellationToken))
        {
            return Problems.FieldError(http, "checklistTemplateId", UnknownTemplateMessage);
        }

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
            time.GetUtcNow());
        db.Jobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);

        events.Enqueue(JobEvent.Created(job, CorrelationId.From(http)));

        var details = await JobDetailsQuery.FindAsync(db, job.Id, JobActor.Administrator(user.Id), cancellationToken)
            ?? throw new InvalidOperationException("The created job could not be read back.");
        return TypedResults.Created($"/api/jobs/{job.Id}", details);
    }
}
