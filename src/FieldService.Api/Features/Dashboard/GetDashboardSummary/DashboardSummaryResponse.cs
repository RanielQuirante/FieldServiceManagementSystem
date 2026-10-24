using FieldService.Api.Domain;

namespace FieldService.Api.Features.Dashboard.GetDashboardSummary;

public sealed record DashboardSummaryResponse(
    DateTimeOffset GeneratedAt,
    DashboardOpenJobs OpenJobs,
    DashboardRange Range,
    DashboardTechnicianCounts Technicians,
    IReadOnlyList<DashboardUnassignedJob> NeedsTechnician,
    IReadOnlyList<DashboardActivityEntry> RecentActivity);

public sealed record DashboardOpenJobs(
    int Total,
    int Unassigned,
    int Assigned,
    int InProgress,
    int OnHold,
    int WithTechnician,
    int InProgressTechnicianCount,
    DateTimeOffset? NextAssignedStart,
    DateTimeOffset? OnHoldSince);

public sealed record DashboardRange(
    DateTimeOffset From,
    DateTimeOffset To,
    int Created,
    int Completed,
    int Cancelled,
    int UnassignedScheduled,
    DateTimeOffset? LastCompletedAt);

public sealed record DashboardTechnicianCounts(int Total, int OnShift);

public sealed record DashboardUnassignedJob(
    Guid Id,
    long Number,
    string Title,
    JobPriority Priority,
    string CustomerName,
    string SiteAddress,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd);

public sealed record DashboardActivityEntry(
    Guid Id,
    DateTimeOffset OccurredAt,
    JobHistoryAction Action,
    Guid JobId,
    long JobNumber,
    JobStatus? FromStatus,
    JobStatus ToStatus,
    UserReference Actor,
    UserReference? Technician,
    string? Note);
