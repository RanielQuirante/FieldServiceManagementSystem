using System.Collections.Concurrent;
using FieldService.Api.Shared.Realtime;

namespace FieldService.Api.Tests.Infrastructure;

public sealed class RecordingJobEventQueue : IJobEventQueue
{
    private readonly ConcurrentQueue<JobEvent> _events = new();

    public Action<JobEvent>? OnEnqueue { get; set; }

    public IReadOnlyList<JobEvent> Events => [.. _events];

    public void Enqueue(JobEvent jobEvent)
    {
        OnEnqueue?.Invoke(jobEvent);
        _events.Enqueue(jobEvent);
    }

    public void Clear()
    {
        _events.Clear();
        OnEnqueue = null;
    }

    public JobEvent SingleOf(string type, Guid jobId)
    {
        return Assert.Single(Events, jobEvent => jobEvent.Type == type && jobEvent.JobId == jobId);
    }

    public void AssertEmpty()
    {
        Assert.Empty(Events);
    }
}
