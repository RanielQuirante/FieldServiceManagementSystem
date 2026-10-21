namespace FieldService.Api.Shared.Realtime;

public sealed class NullJobEventQueue : IJobEventQueue
{
    public void Enqueue(JobEvent jobEvent)
    {
    }
}
