using FieldService.Api.Domain;
using FieldService.Api.Persistence;
using FieldService.Api.Shared.Errors;
using FieldService.Api.Shared.Http;
using FieldService.Api.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Features.Inspections.SaveInspection;

public static class SaveInspectionEndpoint
{
    public static void Map(RouteGroupBuilder inspections) =>
        inspections.MapPut(string.Empty, HandleAsync)
            .WithName("SaveInspection")
            .WithSummary("Save the inspection of a job")
            .WithDescription("Saves any subset of answers to the job's checklist as a draft; the first save creates the inspection. Each listed answer replaces that item's saved answer, readings included; unlisted items stay as they are, and an answer cannot be cleared. Send 1 to 100 answers, each for a different item of the job's checklist. A Fail needs a note and may carry any of the item's readings; a Pass supplies every reading field of its item once; a NotApplicable answer takes no readings. An answer holds a note of at most 1000 characters and at most 10 readings, each from -99999999.99 to 99999999.99 with at most two decimal places. A save changes the job's version. Send the version you last read; a save in which every answer equals the saved one returns the inspection unchanged. A job that is not InProgress, or an inspection that was submitted, returns 409 inspection.locked. A technician can save only the inspections of jobs assigned to them; any other job returns 404. The response carries the inspection and the job's new version.")
            .RequireAuthorization(Policies.Technician)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    public static async Task<Results<Ok<InspectionResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        Guid jobId,
        SaveInspectionRequest request,
        HttpContext http,
        CurrentUser user,
        FieldServiceDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var actor = new JobActor(user.Id, user.Role);
        var job = await db.Jobs.VisibleTo(actor).SingleOrDefaultAsync(entry => entry.Id == jobId, cancellationToken);
        if (job is null)
        {
            return Problems.Of(http, ErrorCodes.JobNotFound);
        }

        var checklist = await db.ChecklistAsync(job.ChecklistTemplateId, cancellationToken);
        var answers = AnswersOf(request);
        if (InspectionRules.Validate(checklist, answers) is { Count: > 0 } errors)
        {
            return Problems.FieldErrors(http, FieldErrorsOf(errors));
        }

        var inspection = await db.Inspections
            .WithAnswersAndReadings()
            .SingleOrDefaultAsync(entry => entry.JobId == job.Id, cancellationToken);
        if (inspection is not null && inspection.HasSameAnswers(answers))
        {
            return TypedResults.Ok(await ReadAsync(db, job.Id, actor, cancellationToken));
        }

        var now = time.GetUtcNow();
        var isFirstSave = inspection is null;
        inspection ??= Inspection.Start(job, now);
        db.Entry(job).Property(entry => entry.Version).OriginalValue = request.Version.GetValueOrDefault();
        if (inspection.SaveAnswers(job, checklist, answers, actor, now) is { } error)
        {
            return error.IsFieldError ? Problems.FieldError(http, error) : Problems.Conflict(http, error);
        }

        if (isFirstSave)
        {
            db.Inspections.Add(inspection);
        }
        else
        {
            db.Entry(inspection).Property(entry => entry.UpdatedAt).IsModified = true;
        }

        db.Entry(job).Property(entry => entry.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(await ReadAsync(db, job.Id, actor, cancellationToken));
    }

    private static List<ChecklistAnswer> AnswersOf(SaveInspectionRequest request) =>
    [
        .. request.Answers.Select(answer => new ChecklistAnswer(
            answer.ChecklistItemId.GetValueOrDefault(),
            answer.Result.GetValueOrDefault(),
            answer.Note,
            [
                .. (answer.Readings ?? []).Select(reading =>
                    new ChecklistReading(reading.ReadingFieldId.GetValueOrDefault(), reading.Value.GetValueOrDefault())),
            ])),
    ];

    private static Dictionary<string, string[]> FieldErrorsOf(IReadOnlyList<DomainError> errors)
    {
        var fieldErrors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var group in errors.GroupBy(error => error.Field ?? ProblemKeyNormalizer.BodyKey, StringComparer.Ordinal))
        {
            fieldErrors[group.Key] = [.. group.Select(error => error.Message)];
        }

        return fieldErrors;
    }

    private static async Task<InspectionResponse> ReadAsync(
        FieldServiceDbContext db,
        Guid jobId,
        JobActor actor,
        CancellationToken cancellationToken) =>
        await InspectionQuery.FindAsync(db, jobId, actor, cancellationToken)
            ?? throw new InvalidOperationException("The inspection could not be read back after the save.");
}
