namespace FieldService.Api.Domain;

public sealed class ChecklistReadingField
{
    private ChecklistReadingField()
    {
    }

    public Guid Id { get; private set; }

    public Guid ChecklistItemId { get; private set; }

    public short Position { get; private set; }

    public string Label { get; private set; } = string.Empty;

    public string Unit { get; private set; } = string.Empty;

    public static ChecklistReadingField Create(
        Guid id,
        Guid checklistItemId,
        short position,
        string label,
        string unit)
    {
        return new ChecklistReadingField
        {
            Id = id,
            ChecklistItemId = checklistItemId,
            Position = position,
            Label = label,
            Unit = unit,
        };
    }
}
