namespace FieldService.Api.Shared.Realtime;

public static partial class RealtimeLog
{
    public const string DispatchedEvent = "job-event.dispatched";

    public const string DispatchFailedEvent = "job-event.dispatch-failed";

    public const string DroppedEvent = "job-event.dropped";

    public const string DrainIncompleteEvent = "job-event.drain-incomplete";

    public const string QueueFull = "queue-full";

    public const string QueueStopped = "queue-stopped";

    public const string Rejected = "rejected";

    public const string Unavailable = "unavailable";

    public const string TimedOut = "timeout";

    public const string CircuitOpen = "circuit-open";

    public const string Unreachable = "unreachable";

    public const string ShuttingDown = "shutdown";

    public const string Failed = "failed";

    [LoggerMessage(
        EventId = 5001,
        EventName = DispatchedEvent,
        Level = LogLevel.Information,
        Message = "Job event {JobEventId} of type {Type} for job {JobId} with correlation id {CorrelationId} was delivered with status {StatusCode}")]
    public static partial void Dispatched(
        ILogger logger,
        Guid jobEventId,
        string type,
        Guid jobId,
        string correlationId,
        int statusCode);

    [LoggerMessage(
        EventId = 5002,
        EventName = DispatchFailedEvent,
        Level = LogLevel.Warning,
        Message = "Job event {JobEventId} of type {Type} for job {JobId} with correlation id {CorrelationId} was not delivered: {Reason}, status {StatusCode}")]
    public static partial void DispatchFailed(
        ILogger logger,
        Guid jobEventId,
        string type,
        Guid jobId,
        string correlationId,
        string reason,
        int? statusCode);

    [LoggerMessage(
        EventId = 5003,
        EventName = DroppedEvent,
        Level = LogLevel.Warning,
        Message = "Job event {JobEventId} of type {Type} for job {JobId} with correlation id {CorrelationId} was dropped: {Reason}")]
    public static partial void Dropped(
        ILogger logger,
        Guid jobEventId,
        string type,
        Guid jobId,
        string correlationId,
        string reason);

    [LoggerMessage(
        EventId = 5004,
        EventName = DrainIncompleteEvent,
        Level = LogLevel.Warning,
        Message = "{Remaining} queued job events were not delivered before shutdown")]
    public static partial void DrainIncomplete(ILogger logger, int remaining);
}
