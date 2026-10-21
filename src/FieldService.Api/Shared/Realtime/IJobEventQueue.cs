namespace FieldService.Api.Shared.Realtime;

public interface IJobEventQueue
{
    void Enqueue(JobEvent jobEvent);
}
