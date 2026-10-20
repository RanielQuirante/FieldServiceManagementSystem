namespace FieldService.Api.Domain;

public sealed class ChecklistItem
{
    private readonly List<ChecklistReadingField> _readingFields = [];

    private ChecklistItem()
    {
    }

    public Guid Id { get; private set; }

    public Guid TemplateId { get; private set; }

    public short Position { get; private set; }

    public string Prompt { get; private set; } = string.Empty;

    public IReadOnlyList<ChecklistReadingField> ReadingFields => _readingFields;

    public static ChecklistItem Create(Guid id, Guid templateId, short position, string prompt)
    {
        return new ChecklistItem
        {
            Id = id,
            TemplateId = templateId,
            Position = position,
            Prompt = prompt,
        };
    }
}
