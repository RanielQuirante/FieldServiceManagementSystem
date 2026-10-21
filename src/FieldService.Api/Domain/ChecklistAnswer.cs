namespace FieldService.Api.Domain;

public sealed record ChecklistAnswer(
    Guid ChecklistItemId,
    InspectionResult Result,
    string? Note,
    IReadOnlyList<ChecklistReading> Readings);
