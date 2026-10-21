using FieldService.Api.Domain;

namespace FieldService.Api.Features.Jobs;

public sealed record JobHistoryEntryResponse(
    Guid Id,
    JobHistoryAction Action,
    JobStatus? FromStatus,
    JobStatus ToStatus,
    UserReference Actor,
    UserReference? Technician,
    string? Note,
    DateTimeOffset OccurredAt);
