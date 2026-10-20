namespace FieldService.Api.Domain;

public sealed class JobHistoryEntry
{
    private JobHistoryEntry()
    {
    }

    public Guid Id { get; private init; }

    public Guid JobId { get; private init; }

    public JobHistoryAction Action { get; private init; }

    public JobStatus? FromStatus { get; private init; }

    public JobStatus ToStatus { get; private init; }

    public Guid ActorUserId { get; private init; }

    public Guid? TechnicianId { get; private init; }

    public string? Note { get; private init; }

    public DateTimeOffset OccurredAt { get; private init; }

    public static JobHistoryEntry Create(
        Guid jobId,
        JobHistoryAction action,
        JobStatus? fromStatus,
        JobStatus toStatus,
        Guid actorUserId,
        Guid? technicianId,
        string? note,
        DateTimeOffset occurredAt)
    {
        return new JobHistoryEntry
        {
            Id = Guid.CreateVersion7(occurredAt),
            JobId = jobId,
            Action = action,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            ActorUserId = actorUserId,
            TechnicianId = technicianId,
            Note = note,
            OccurredAt = occurredAt,
        };
    }
}
