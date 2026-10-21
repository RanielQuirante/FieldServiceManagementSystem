namespace FieldService.Api.Domain;

public sealed record JobTransition(
    JobCommand Command,
    JobStatus From,
    JobStatus To,
    JobHistoryAction Action,
    JobTransitionActor Actor,
    bool RequiresNote);
