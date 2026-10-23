using FieldService.Api.Domain;
using FieldService.Api.Shared.Realtime;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace FieldService.Api.Tests.Shared.Realtime;

public sealed class JobEventQueueTests
{
    [Fact]
    public void TheQueueHoldsOneThousandEventsInOrderAndTheNextDropsTheOldestWithOneWarning()
    {
        using var logs = new RealtimeLogs();
        var queue = new JobEventQueue(logs.Factory.CreateLogger<JobEventQueue>());
        var events = Enumerable.Range(1, JobEventQueue.Capacity + 1).Select(NewEvent).ToList();

        foreach (var jobEvent in events)
        {
            queue.Enqueue(jobEvent);
        }

        Assert.Equal(JobEventQueue.Capacity, queue.Reader.Count);
        var dropped = Assert.Single(logs.Sink.Named(RealtimeLog.DroppedEvent));
        Assert.Equal(LogEventLevel.Warning, dropped.Level);
        Assert.Equal(events[0].Id.ToString(), CapturingLogSink.Scalar(dropped, "JobEventId"));
        Assert.Equal(events[0].Type, CapturingLogSink.Scalar(dropped, "Type"));
        Assert.Equal(events[0].JobId.ToString(), CapturingLogSink.Scalar(dropped, "JobId"));
        Assert.Equal(events[0].CorrelationId, CapturingLogSink.Scalar(dropped, "CorrelationId"));
        Assert.Equal(RealtimeLog.QueueFull, CapturingLogSink.Scalar(dropped, "Reason"));
        var remaining = new List<JobEvent>();
        while (queue.Reader.TryRead(out var jobEvent))
        {
            remaining.Add(jobEvent);
        }

        Assert.Equal(events.Skip(1), remaining);
    }

    [Fact]
    public void EnqueueNeverBlocksOrThrowsAndAfterTheQueueIsCompletedTheEventIsLoggedAsDropped()
    {
        using var logs = new RealtimeLogs();
        var queue = new JobEventQueue(logs.Factory.CreateLogger<JobEventQueue>());
        var late = NewEvent(1);

        queue.Complete();
        var exception = Record.Exception(() => queue.Enqueue(late));

        Assert.Null(exception);
        Assert.Equal(0, queue.Reader.Count);
        var dropped = Assert.Single(logs.Sink.Named(RealtimeLog.DroppedEvent));
        Assert.Equal(RealtimeLog.QueueStopped, CapturingLogSink.Scalar(dropped, "Reason"));
        Assert.Equal(late.Id.ToString(), CapturingLogSink.Scalar(dropped, "JobEventId"));
    }

    [Fact]
    public async Task ConcurrentWritersNeverBlockAndEveryEventIsKeptOrLoggedAsDropped()
    {
        using var logs = new RealtimeLogs();
        var queue = new JobEventQueue(logs.Factory.CreateLogger<JobEventQueue>());

        await Task.WhenAll(Enumerable.Range(0, 4).Select(writer => Task.Run(
            () =>
            {
                for (var index = 0; index < 600; index++)
                {
                    queue.Enqueue(NewEvent((writer * 1_000) + index));
                }
            },
            TestContext.Current.CancellationToken)));

        Assert.Equal(JobEventQueue.Capacity, queue.Reader.Count);
        Assert.Equal(2_400 - JobEventQueue.Capacity, logs.Sink.Named(RealtimeLog.DroppedEvent).Count);
    }

    private static JobEvent NewEvent(int number)
    {
        var occurredAt = new DateTimeOffset(2026, 10, 19, 14, 42, 0, TimeSpan.Zero).AddSeconds(number);
        return new JobEvent(
            Guid.CreateVersion7(occurredAt),
            JobEventTypes.StatusChanged,
            JobEvent.CurrentVersion,
            occurredAt,
            $"queue-{number}",
            Guid.CreateVersion7(occurredAt),
            1000 + number,
            JobStatus.InProgress,
            JobStatus.Assigned);
    }
}
