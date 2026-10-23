using FieldService.Api.Domain;

namespace FieldService.Api.Features.Inspections;

public sealed record InspectionResponse(
    Guid JobId,
    long JobNumber,
    JobStatus JobStatus,
    ChecklistTemplateReference Template,
    IReadOnlyList<InspectionItemResponse> Items,
    InspectionProgress Progress,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? SubmittedAt,
    string? TechnicianNotes,
    uint Version,
    InspectionAffordances Affordances);

public sealed record InspectionItemResponse(
    Guid ChecklistItemId,
    int Position,
    string Prompt,
    IReadOnlyList<InspectionReadingFieldResponse> ReadingFields,
    InspectionAnswerResponse? Answer);

public sealed record InspectionReadingFieldResponse(Guid ReadingFieldId, int Position, string Label, string Unit);

public sealed record InspectionAnswerResponse(
    InspectionResult Result,
    string? Note,
    IReadOnlyList<InspectionReadingResponse> Readings,
    DateTimeOffset AnsweredAt);

public sealed record InspectionReadingResponse(Guid ReadingFieldId, decimal Value);

public sealed record InspectionProgress(
    int Answered,
    int Total,
    int PassCount,
    int FailCount,
    int NotApplicableCount);

public sealed record InspectionAffordances(bool CanEdit, SubmitAffordance CanSubmit);
