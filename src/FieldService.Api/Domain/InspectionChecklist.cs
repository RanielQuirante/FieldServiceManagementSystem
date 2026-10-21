namespace FieldService.Api.Domain;

public sealed class InspectionChecklist
{
    private readonly Dictionary<Guid, InspectionChecklistItem> _byId;

    private InspectionChecklist(Guid templateId, IReadOnlyList<InspectionChecklistItem> items)
    {
        TemplateId = templateId;
        Items = items;
        _byId = items.ToDictionary(item => item.Id);
    }

    public Guid TemplateId { get; }

    public IReadOnlyList<InspectionChecklistItem> Items { get; }

    public int Total => Items.Count;

    public static InspectionChecklist From(ChecklistTemplate template) =>
        From(template.Id, template.Items, template.Items.SelectMany(item => item.ReadingFields));

    public static InspectionChecklist From(
        Guid templateId,
        IEnumerable<ChecklistItem> items,
        IEnumerable<ChecklistReadingField> readingFields)
    {
        var fieldsByItem = readingFields
            .GroupBy(field => field.ChecklistItemId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Guid>)[.. group.OrderBy(field => field.Position).Select(field => field.Id)]);
        IReadOnlyList<InspectionChecklistItem> checklistItems =
        [
            .. items
                .Where(item => item.TemplateId == templateId)
                .OrderBy(item => item.Position)
                .Select(item => new InspectionChecklistItem(
                    item.Id,
                    item.Position,
                    fieldsByItem.GetValueOrDefault(item.Id) ?? [])),
        ];
        if (checklistItems.Count == 0)
        {
            throw new ArgumentException("The checklist template has no items.", nameof(templateId));
        }

        return new InspectionChecklist(templateId, checklistItems);
    }

    public InspectionChecklistItem? Find(Guid checklistItemId) => _byId.GetValueOrDefault(checklistItemId);
}
