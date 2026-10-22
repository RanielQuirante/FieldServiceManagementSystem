using FieldService.Api.Domain;

namespace FieldService.Api.Features.Jobs;

public sealed record JobDetailsResponse(
    Guid Id,
    long Number,
    string Title,
    string Description,
    JobPriority Priority,
    JobStatus Status,
    string CustomerName,
    string SiteAddress,
    string? SiteAccessNotes,
    string? ContactName,
    string? ContactPhone,
    string? Equipment,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd,
    ChecklistTemplateReference ChecklistTemplate,
    JobTechnicianReference? AssignedTechnician,
    UserReference CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    IReadOnlyList<JobHistoryEntryResponse> History,
    uint Version,
    JobAffordances Affordances);
