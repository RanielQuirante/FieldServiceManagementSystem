using FieldService.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Persistence;

public static class InspectionLoading
{
    public static Task<List<ChecklistItem>> ChecklistItemsAsync(
        this FieldServiceDbContext db,
        Guid templateId,
        CancellationToken cancellationToken) =>
        db.Set<ChecklistItem>()
            .AsNoTracking()
            .Include(item => item.ReadingFields)
            .Where(item => item.TemplateId == templateId)
            .OrderBy(item => item.Position)
            .ToListAsync(cancellationToken);

    public static async Task<InspectionChecklist> ChecklistAsync(
        this FieldServiceDbContext db,
        Guid templateId,
        CancellationToken cancellationToken)
    {
        var items = await db.ChecklistItemsAsync(templateId, cancellationToken);
        return ChecklistOf(templateId, items);
    }

    public static InspectionChecklist ChecklistOf(Guid templateId, IReadOnlyList<ChecklistItem> items) =>
        InspectionChecklist.From(templateId, items, items.SelectMany(item => item.ReadingFields));

    public static IQueryable<Inspection> WithAnswersAndReadings(this IQueryable<Inspection> inspections) =>
        inspections
            .Include(inspection => inspection.Answers)
            .ThenInclude(answer => answer.Readings)
            .AsSingleQuery();
}
