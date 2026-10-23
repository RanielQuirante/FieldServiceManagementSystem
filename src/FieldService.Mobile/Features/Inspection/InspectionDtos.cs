using FieldService.Mobile.Features.Jobs;

namespace FieldService.Mobile.Features.Inspection;

public enum InspectionResult
{
    Unknown,
    Pass,
    Fail,
    NotApplicable,
}

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
    InspectionAffordances Affordances)
{
    public IReadOnlyList<InspectionItemResponse> Items { get; init; } = Items ?? [];

    public override string ToString() => $"{nameof(InspectionResponse)} {JobId}";
}

public sealed record InspectionItemResponse(
    Guid ChecklistItemId,
    int Position,
    string Prompt,
    IReadOnlyList<InspectionReadingFieldResponse> ReadingFields,
    InspectionAnswerResponse? Answer)
{
    public IReadOnlyList<InspectionReadingFieldResponse> ReadingFields { get; init; } = ReadingFields ?? [];

    public override string ToString() => $"{nameof(InspectionItemResponse)} {ChecklistItemId}";
}

public sealed record InspectionReadingFieldResponse(Guid ReadingFieldId, int Position, string Label, string Unit);

public sealed record InspectionAnswerResponse(
    InspectionResult Result,
    string? Note,
    IReadOnlyList<InspectionReadingResponse> Readings,
    DateTimeOffset AnsweredAt)
{
    public IReadOnlyList<InspectionReadingResponse> Readings { get; init; } = Readings ?? [];

    public override string ToString() => $"{nameof(InspectionAnswerResponse)} {Result}";
}

public sealed record InspectionReadingResponse(Guid ReadingFieldId, decimal Value);

public sealed record InspectionProgress(int Answered, int Total, int PassCount, int FailCount, int NotApplicableCount);

public sealed record InspectionAffordances(bool CanEdit, SubmitAffordance CanSubmit)
{
    public SubmitAffordance CanSubmit { get; init; } = CanSubmit ?? SubmitAffordance.None;
}

public sealed record SaveInspectionRequest(IReadOnlyList<InspectionAnswerInput> Answers, uint Version)
{
    public override string ToString() => $"{nameof(SaveInspectionRequest)} {Answers.Count}";
}

public sealed record InspectionAnswerInput(
    Guid ChecklistItemId,
    InspectionResult Result,
    string? Note,
    IReadOnlyList<InspectionReadingInput>? Readings)
{
    public override string ToString() => $"{nameof(InspectionAnswerInput)} {ChecklistItemId} {Result}";
}

public sealed record InspectionReadingInput(Guid ReadingFieldId, decimal Value);

public sealed record SubmitJobRequest(string? TechnicianNotes, bool SiteSafetyConfirmed, uint Version)
{
    public override string ToString() => $"{nameof(SubmitJobRequest)} {SiteSafetyConfirmed}";
}
