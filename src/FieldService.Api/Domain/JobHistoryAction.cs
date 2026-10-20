namespace FieldService.Api.Domain;

public enum JobHistoryAction
{
    Created,
    Assigned,
    Started,
    PutOnHold,
    Resumed,
    Completed,
    Cancelled,
}
