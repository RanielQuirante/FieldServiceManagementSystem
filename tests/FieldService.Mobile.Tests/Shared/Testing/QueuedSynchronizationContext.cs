using System.Collections.Concurrent;

namespace FieldService.Mobile.Tests.Shared.Testing;

public sealed class QueuedSynchronizationContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _posted = new();

    public static T CreateOn<T>(QueuedSynchronizationContext context, Func<T> create)
    {
        ArgumentNullException.ThrowIfNull(create);

        var previous = Current;
        SetSynchronizationContext(context);
        try
        {
            return create();
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }

    public override void Post(SendOrPostCallback d, object? state) => _posted.Enqueue((d, state));

    public int RunPending()
    {
        var count = 0;
        while (_posted.TryDequeue(out var next))
        {
            next.Callback(next.State);
            count++;
        }

        return count;
    }
}
