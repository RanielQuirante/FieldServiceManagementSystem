using FieldService.Api.Domain;

namespace FieldService.Api.Features.Technicians.GetAssignedJobs;

public sealed record GetAssignedJobsResponse(IReadOnlyList<AssignedJobResponse> Items);

public sealed record AssignedJobResponse(
    Guid Id,
    long Number,
    string Title,
    JobPriority Priority,
    JobStatus Status,
    string CustomerName,
    string SiteAddress,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset UpdatedAt,
    uint Version);
