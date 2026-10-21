namespace FieldService.Api.Domain;

public sealed record InspectionChecklistItem(Guid Id, short Position, IReadOnlyList<Guid> ReadingFieldIds);
