namespace FieldService.Api.Domain;

public sealed class Job
{
    private readonly List<JobHistoryEntry> _history = [];

    private Job()
    {
    }

    public Guid Id { get; private set; }

    public long Number { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public JobPriority Priority { get; private set; }

    public string CustomerName { get; private set; } = string.Empty;

    public string SiteAddress { get; private set; } = string.Empty;

    public string? SiteAccessNotes { get; private set; }

    public string? ContactName { get; private set; }

    public string? ContactPhone { get; private set; }

    public string? Equipment { get; private set; }

    public DateTimeOffset ScheduledStart { get; private set; }

    public DateTimeOffset ScheduledEnd { get; private set; }

    public Guid ChecklistTemplateId { get; private set; }

    public JobStatus Status { get; private set; }

    public Guid? AssignedTechnicianId { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? AssignedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyList<JobHistoryEntry> History => _history;

    public static Job Create(JobDetails details, Guid createdByUserId, DateTimeOffset now)
    {
        return new Job
        {
            Id = Guid.CreateVersion7(now),
            Title = details.Title,
            Description = details.Description,
            Priority = details.Priority,
            CustomerName = details.CustomerName,
            SiteAddress = details.SiteAddress,
            SiteAccessNotes = details.SiteAccessNotes,
            ContactName = details.ContactName,
            ContactPhone = details.ContactPhone,
            Equipment = details.Equipment,
            ScheduledStart = details.ScheduledStart,
            ScheduledEnd = details.ScheduledEnd,
            ChecklistTemplateId = details.ChecklistTemplateId,
            Status = JobStatus.Unassigned,
            CreatedByUserId = createdByUserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
