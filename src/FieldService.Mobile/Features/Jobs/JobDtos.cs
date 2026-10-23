namespace FieldService.Mobile.Features.Jobs;

public enum JobStatus
{
    Unknown,
    Unassigned,
    Assigned,
    InProgress,
    OnHold,
    Completed,
    Cancelled,
}

public enum JobPriority
{
    Unknown,
    Low,
    Normal,
    High,
    Urgent,
}

public enum JobHistoryAction
{
    Unknown,
    Created,
    Assigned,
    Started,
    PutOnHold,
    Resumed,
    Completed,
    Cancelled,
}

public enum Trade
{
    Unknown,
    Hvac,
    Electrical,
    Plumbing,
    FireSafety,
}

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
    uint Version)
{
    public override string ToString() => $"{nameof(AssignedJobResponse)} {Id}";
}

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
    JobAffordances Affordances)
{
    public override string ToString() => $"{nameof(JobDetailsResponse)} {Id}";
}

public sealed record ChecklistTemplateReference(Guid Id, string Name, Trade Trade);

public sealed record JobTechnicianReference(Guid Id, string DisplayName, Trade Trade)
{
    public override string ToString() => $"{nameof(JobTechnicianReference)} {Id}";
}

public sealed record UserReference(Guid Id, string DisplayName)
{
    public override string ToString() => $"{nameof(UserReference)} {Id}";
}

public sealed record JobHistoryEntryResponse(
    Guid Id,
    JobHistoryAction Action,
    JobStatus? FromStatus,
    JobStatus ToStatus,
    UserReference Actor,
    UserReference? Technician,
    string? Note,
    DateTimeOffset OccurredAt)
{
    public override string ToString() => $"{nameof(JobHistoryEntryResponse)} {Id}";
}

public sealed record JobAffordances(IReadOnlyList<AllowedStatusChange> AllowedStatusChanges, bool CanAssign)
{
    public IReadOnlyList<AllowedStatusChange> AllowedStatusChanges { get; init; } = AllowedStatusChanges ?? [];

    public SubmitAffordance CanSubmit { get; init => field = value ?? SubmitAffordance.None; } = SubmitAffordance.None;
}

public sealed record SubmitAffordance(bool Allowed, string? BlockingCode, int Answered, int Total)
{
    public const string IncompleteCode = "inspection.incomplete";
    public const string InvalidTransitionCode = "job.invalid-transition";

    public static SubmitAffordance None { get; } = new(false, null, 0, 0);
}

public sealed record AllowedStatusChange(JobStatus Status, bool RequiresNote);

public sealed record UpdateJobStatusRequest(JobStatus Status, string? Note, uint Version)
{
    public override string ToString() => $"{nameof(UpdateJobStatusRequest)} {Status}";
}
