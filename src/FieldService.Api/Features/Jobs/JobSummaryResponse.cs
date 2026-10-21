using FieldService.Api.Domain;

namespace FieldService.Api.Features.Jobs;

public sealed record JobSummaryResponse(
    Guid Id,
    long Number,
    string Title,
    JobPriority Priority,
    JobStatus Status,
    string CustomerName,
    string SiteAddress,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd,
    JobTechnicianReference? AssignedTechnician,
    DateTimeOffset UpdatedAt,
    uint Version);
