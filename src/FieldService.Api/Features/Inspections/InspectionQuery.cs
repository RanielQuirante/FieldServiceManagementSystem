using FieldService.Api.Domain;
using FieldService.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Features.Inspections;

public static class InspectionQuery
{
    public static async Task<InspectionResponse?> FindAsync(
        FieldServiceDbContext db,
        Guid jobId,
        JobActor actor,
        CancellationToken cancellationToken)
    {
        var row = await (
            from job in db.Jobs.AsNoTracking().VisibleTo(actor)
            where job.Id == jobId
            join template in db.ChecklistTemplates on job.ChecklistTemplateId equals template.Id
            select new
            {
                Job = job,
                Template = new ChecklistTemplateReference(template.Id, template.Name, template.Trade),
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var details = row.Job;
        var items = await db.ChecklistItemsAsync(details.ChecklistTemplateId, cancellationToken);
        var inspection = await db.Inspections
            .AsNoTracking()
            .WithAnswersAndReadings()
            .SingleOrDefaultAsync(entry => entry.JobId == jobId, cancellationToken);

        var checklist = InspectionLoading.ChecklistOf(details.ChecklistTemplateId, items);
        var completeness = InspectionCompleteness.Of(checklist, inspection);
        var submit = JobCapabilities.For(details, actor, completeness).CanSubmit;
        var answers = new Dictionary<Guid, InspectionAnswer>();
        foreach (var answer in inspection?.Answers ?? [])
        {
            answers[answer.ChecklistItemId] = answer;
        }

        return new InspectionResponse(
            details.Id,
            details.Number,
            details.Status,
            row.Template,
            [.. items.Select(item => ItemOf(item, answers.GetValueOrDefault(item.Id)))],
            new InspectionProgress(
                completeness.Answered,
                completeness.Total,
                completeness.PassCount,
                completeness.FailCount,
                completeness.NotApplicableCount),
            details.StartedAt,
            details.CompletedAt,
            inspection?.UpdatedAt,
            inspection?.SubmittedAt,
            inspection?.TechnicianNotes,
            details.Version,
            new InspectionAffordances(
                Inspection.CanEdit(details, inspection, actor),
                new SubmitAffordance(submit.Allowed, submit.BlockingCode, submit.Answered, submit.Total)));
    }

    private static InspectionItemResponse ItemOf(ChecklistItem item, InspectionAnswer? answer)
    {
        var fields = item.ReadingFields.OrderBy(field => field.Position).ToList();
        var positions = fields.ToDictionary(field => field.Id, field => field.Position);
        return new InspectionItemResponse(
            item.Id,
            item.Position,
            item.Prompt,
            [.. fields.Select(field => new InspectionReadingFieldResponse(field.Id, field.Position, field.Label, field.Unit))],
            answer is null
                ? null
                : new InspectionAnswerResponse(
                    answer.Result,
                    answer.Note,
                    [
                        .. answer.Readings
                            .OrderBy(reading => positions.GetValueOrDefault(reading.ReadingFieldId, short.MaxValue))
                            .ThenBy(reading => reading.ReadingFieldId)
                            .Select(reading => new InspectionReadingResponse(reading.ReadingFieldId, reading.Value)),
                    ],
                    answer.AnsweredAt));
    }
}
