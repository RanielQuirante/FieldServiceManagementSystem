using FieldService.Api.Domain;
using FieldService.Api.Shared.Observability;

namespace FieldService.Api.Shared.Realtime;

public sealed record JobEvent(
    Guid Id,
    string Type,
    int Version,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    Guid JobId,
    long JobNumber,
    JobStatus Status,
    JobStatus? PreviousStatus)
{
    public const int CurrentVersion = 1;

    public static JobEvent Created(Job job, CorrelationId correlationId) =>
        For(JobEventTypes.Created, job, previousStatus: null, correlationId);

    public static JobEvent Assigned(Job job, JobStatus previousStatus, CorrelationId correlationId) =>
        For(JobEventTypes.Assigned, job, previousStatus, correlationId);

    public static JobEvent StatusChanged(Job job, JobStatus previousStatus, CorrelationId correlationId) =>
        For(JobEventTypes.StatusChanged, job, previousStatus, correlationId);

    private static JobEvent For(string type, Job job, JobStatus? previousStatus, CorrelationId correlationId) =>
        new(
            Guid.CreateVersion7(job.UpdatedAt),
            type,
            CurrentVersion,
            job.UpdatedAt,
            correlationId.Value,
            job.Id,
            job.Number,
            job.Status,
            previousStatus);
}
