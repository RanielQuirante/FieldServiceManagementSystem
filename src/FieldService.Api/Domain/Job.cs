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
        var at = UtcInstant.Truncate(now);
        var scheduledStart = UtcInstant.Truncate(details.ScheduledStart);
        var scheduledEnd = UtcInstant.Truncate(details.ScheduledEnd);
        if (scheduledEnd <= scheduledStart)
        {
            throw new ArgumentException("The scheduled end must be after the scheduled start.", nameof(details));
        }

        var job = new Job
        {
            Id = Guid.CreateVersion7(at),
            Title = DomainText.Required(details.Title, nameof(details.Title)),
            Description = DomainText.Required(details.Description, nameof(details.Description)),
            Priority = details.Priority,
            CustomerName = DomainText.Required(details.CustomerName, nameof(details.CustomerName)),
            SiteAddress = DomainText.Required(details.SiteAddress, nameof(details.SiteAddress)),
            SiteAccessNotes = DomainText.Optional(details.SiteAccessNotes),
            ContactName = DomainText.Optional(details.ContactName),
            ContactPhone = DomainText.Optional(details.ContactPhone),
            Equipment = DomainText.Optional(details.Equipment),
            ScheduledStart = scheduledStart,
            ScheduledEnd = scheduledEnd,
            ChecklistTemplateId = details.ChecklistTemplateId,
            Status = JobStatus.Unassigned,
            CreatedByUserId = createdByUserId,
            CreatedAt = at,
            UpdatedAt = at,
        };
        job._history.Add(JobHistoryEntry.Create(
            job.Id,
            JobHistoryAction.Created,
            fromStatus: null,
            JobStatus.Unassigned,
            createdByUserId,
            technicianId: null,
            note: null,
            at));
        return job;
    }

    public bool IsAssignedTo(Guid technicianId) => AssignedTechnicianId == technicianId;

    public DomainError? AssignTo(Technician technician, JobActor actor, DateTimeOffset now) =>
        AssignTo(JobAssignee.Of(technician), actor, now);

    public DomainError? AssignTo(JobAssignee assignee, JobActor actor, DateTimeOffset now)
    {
        var transition = JobTransitions.Find(JobCommand.Assign, this, JobStatus.Assigned, actor);
        if (transition is null)
        {
            return actor.Role == UserRole.Administrator
                ? DomainErrors.NotAssignable(this)
                : DomainErrors.InvalidTransition(this, JobStatus.Assigned);
        }

        if (Status == JobStatus.Assigned && IsAssignedTo(assignee.TechnicianId))
        {
            return null;
        }

        if (!assignee.IsAvailableForAssignment)
        {
            return DomainErrors.TechnicianUnavailable();
        }

        var at = UtcInstant.Truncate(now);
        AssignedTechnicianId = assignee.TechnicianId;
        AssignedAt = at;
        Record(transition, actor, assignee.TechnicianId, note: null, at);
        return null;
    }

    public DomainError? ChangeStatus(JobStatus target, string? note, JobActor actor, DateTimeOffset now)
    {
        var transition = JobTransitions.Find(JobCommand.ChangeStatus, this, target, actor);
        if (transition is null)
        {
            return DomainErrors.InvalidTransition(this, target);
        }

        var storedNote = DomainText.Optional(note);
        if (transition.RequiresNote && !IsValidNote(storedNote))
        {
            return DomainErrors.Field(
                "note",
                $"Enter a note of at most {JobTransitions.MaxNoteLength} characters to put the job on hold.");
        }

        var at = UtcInstant.Truncate(now);
        if (transition.Action == JobHistoryAction.Started)
        {
            StartedAt ??= at;
        }

        Record(transition, actor, technicianId: null, transition.RequiresNote ? storedNote : null, at);
        return null;
    }

    public DomainError? Submit(
        Inspection? inspection,
        InspectionChecklist checklist,
        string? technicianNotes,
        JobActor actor,
        DateTimeOffset now)
    {
        var transition = JobTransitions.Find(JobCommand.Submit, this, JobStatus.Completed, actor);
        if (transition is null)
        {
            return DomainErrors.InvalidTransition(this, JobStatus.Completed);
        }

        EnsureChecklistOfThisJob(checklist);
        if (inspection is not null && inspection.JobId != Id)
        {
            throw new ArgumentException("The inspection belongs to another job.", nameof(inspection));
        }

        if (inspection is { IsSubmitted: true })
        {
            return DomainErrors.InspectionSubmitted();
        }

        var completeness = InspectionCompleteness.Of(checklist, inspection);
        if (inspection is null || !completeness.IsComplete)
        {
            return DomainErrors.InspectionIncomplete(completeness);
        }

        var storedNotes = DomainText.Optional(technicianNotes);
        if (storedNotes is { Length: > Inspection.MaxTechnicianNotesLength })
        {
            return DomainErrors.Field(
                "technicianNotes",
                $"Enter completion notes of at most {Inspection.MaxTechnicianNotesLength} characters.");
        }

        var at = UtcInstant.Truncate(now);
        CompletedAt = at;
        Record(transition, actor, technicianId: null, note: null, at);
        inspection.MarkSubmitted(storedNotes, at);
        return null;
    }

    public DomainError? Cancel(string? reason, JobActor actor, DateTimeOffset now)
    {
        var transition = JobTransitions.Find(JobCommand.Cancel, this, JobStatus.Cancelled, actor);
        if (transition is null)
        {
            return DomainErrors.InvalidTransition(this, JobStatus.Cancelled);
        }

        var storedReason = DomainText.Optional(reason);
        if (!IsValidNote(storedReason))
        {
            return DomainErrors.Field(
                "reason",
                $"Enter a reason of at most {JobTransitions.MaxNoteLength} characters to cancel the job.");
        }

        var at = UtcInstant.Truncate(now);
        CancelledAt = at;
        Record(transition, actor, technicianId: null, storedReason, at);
        return null;
    }

    internal void TouchForInspection(DateTimeOffset at)
    {
        UpdatedAt = at;
    }

    internal void EnsureChecklistOfThisJob(InspectionChecklist checklist)
    {
        if (checklist.TemplateId != ChecklistTemplateId)
        {
            throw new ArgumentException("The checklist is not the job's checklist template.", nameof(checklist));
        }
    }

    private static bool IsValidNote(string? note) => note is { Length: > 0 and <= JobTransitions.MaxNoteLength };

    private void Record(JobTransition transition, JobActor actor, Guid? technicianId, string? note, DateTimeOffset at)
    {
        var fromStatus = Status;
        Status = transition.To;
        UpdatedAt = at;
        _history.Add(JobHistoryEntry.Create(
            Id,
            transition.Action,
            fromStatus,
            transition.To,
            actor.UserId,
            technicianId,
            note,
            at));
    }
}
