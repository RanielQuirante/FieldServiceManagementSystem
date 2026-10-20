namespace FieldService.Api.Domain;

public sealed class InspectionReading
{
    private InspectionReading()
    {
    }

    public Guid InspectionId { get; private set; }

    public Guid ChecklistItemId { get; private set; }

    public Guid ReadingFieldId { get; private set; }

    public decimal Value { get; private set; }

    public static InspectionReading Create(
        Guid inspectionId,
        Guid checklistItemId,
        Guid readingFieldId,
        decimal value)
    {
        return new InspectionReading
        {
            InspectionId = inspectionId,
            ChecklistItemId = checklistItemId,
            ReadingFieldId = readingFieldId,
            Value = value,
        };
    }
}
