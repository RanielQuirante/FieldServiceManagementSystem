using FieldService.Api.Domain;

namespace FieldService.Api.Shared.Realtime;

public sealed record JobEventEnvelope(
    Guid Id,
    string Type,
    int Version,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    JobEventData Data)
{
    public static JobEventEnvelope From(JobEvent jobEvent) =>
        new(
            jobEvent.Id,
            jobEvent.Type,
            jobEvent.Version,
            jobEvent.OccurredAt,
            jobEvent.CorrelationId,
            new JobEventData(jobEvent.JobId, jobEvent.JobNumber, jobEvent.Status, jobEvent.PreviousStatus));
}

public sealed record JobEventData(Guid JobId, long JobNumber, JobStatus Status, JobStatus? PreviousStatus);
