namespace FieldService.Api.Domain;

public sealed class InspectionAnswer
{
    private readonly List<InspectionReading> _readings = [];

    private InspectionAnswer()
    {
    }

    public Guid InspectionId { get; private set; }

    public Guid ChecklistItemId { get; private set; }

    public InspectionResult Result { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset AnsweredAt { get; private set; }

    public IReadOnlyList<InspectionReading> Readings => _readings;

    public static InspectionAnswer Create(
        Guid inspectionId,
        Guid checklistItemId,
        InspectionResult result,
        string? note,
        DateTimeOffset answeredAt)
    {
        return new InspectionAnswer
        {
            InspectionId = inspectionId,
            ChecklistItemId = checklistItemId,
            Result = result,
            Note = note,
            AnsweredAt = answeredAt,
        };
    }
}
