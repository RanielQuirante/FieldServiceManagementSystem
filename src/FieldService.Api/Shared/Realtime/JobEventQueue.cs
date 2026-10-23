using System.Threading.Channels;

namespace FieldService.Api.Shared.Realtime;

public sealed class JobEventQueue(ILogger<JobEventQueue> logger) : IJobEventQueue
{
    public const int Capacity = 1_000;

    private readonly Channel<JobEvent> _channel = Channel.CreateBounded<JobEvent>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        },
        dropped => LogDropped(logger, dropped, RealtimeLog.QueueFull));

    public ChannelReader<JobEvent> Reader => _channel.Reader;

    public void Enqueue(JobEvent jobEvent)
    {
        if (!_channel.Writer.TryWrite(jobEvent))
        {
            LogDropped(logger, jobEvent, RealtimeLog.QueueStopped);
        }
    }

    public void Complete() => _channel.Writer.TryComplete();

    private static void LogDropped(ILogger logger, JobEvent jobEvent, string reason) =>
        RealtimeLog.Dropped(logger, jobEvent.Id, jobEvent.Type, jobEvent.JobId, jobEvent.CorrelationId, reason);
}
