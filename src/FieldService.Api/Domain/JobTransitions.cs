namespace FieldService.Api.Domain;

public static class JobTransitions
{
    public const int MaxNoteLength = 500;

    public static IReadOnlyList<JobStatus> Statuses { get; } = Enum.GetValues<JobStatus>();

    public static IReadOnlyList<JobTransition> All { get; } =
    [
        new(JobCommand.Assign, JobStatus.Unassigned, JobStatus.Assigned, JobHistoryAction.Assigned, JobTransitionActor.Administrator, RequiresNote: false),
        new(JobCommand.Assign, JobStatus.Assigned, JobStatus.Assigned, JobHistoryAction.Assigned, JobTransitionActor.Administrator, RequiresNote: false),
        new(JobCommand.ChangeStatus, JobStatus.Assigned, JobStatus.InProgress, JobHistoryAction.Started, JobTransitionActor.AssignedTechnician, RequiresNote: false),
        new(JobCommand.ChangeStatus, JobStatus.InProgress, JobStatus.OnHold, JobHistoryAction.PutOnHold, JobTransitionActor.AssignedTechnician, RequiresNote: true),
        new(JobCommand.ChangeStatus, JobStatus.OnHold, JobStatus.InProgress, JobHistoryAction.Resumed, JobTransitionActor.AssignedTechnician, RequiresNote: false),
        new(JobCommand.Submit, JobStatus.InProgress, JobStatus.Completed, JobHistoryAction.Completed, JobTransitionActor.AssignedTechnician, RequiresNote: false),
        new(JobCommand.Cancel, JobStatus.Unassigned, JobStatus.Cancelled, JobHistoryAction.Cancelled, JobTransitionActor.Administrator, RequiresNote: true),
        new(JobCommand.Cancel, JobStatus.Assigned, JobStatus.Cancelled, JobHistoryAction.Cancelled, JobTransitionActor.Administrator, RequiresNote: true),
        new(JobCommand.Cancel, JobStatus.InProgress, JobStatus.Cancelled, JobHistoryAction.Cancelled, JobTransitionActor.Administrator, RequiresNote: true),
        new(JobCommand.Cancel, JobStatus.OnHold, JobStatus.Cancelled, JobHistoryAction.Cancelled, JobTransitionActor.Administrator, RequiresNote: true),
    ];

    public static JobTransition? Find(JobCommand command, Job job, JobStatus target, JobActor actor) =>
        All.FirstOrDefault(transition =>
            transition.Command == command
            && transition.From == job.Status
            && transition.To == target
            && Permits(transition.Actor, job, actor));

    public static IEnumerable<JobTransition> Available(Job job, JobActor actor) =>
        All.Where(transition => transition.From == job.Status && Permits(transition.Actor, job, actor));

    public static bool IsAllowed(JobStatus from, JobStatus to, JobTransitionActor actor) =>
        All.Any(transition => transition.From == from && transition.To == to && transition.Actor == actor);

    public static bool Permits(JobTransitionActor required, Job job, JobActor actor) => required switch
    {
        JobTransitionActor.Administrator => actor.Role == UserRole.Administrator,
        JobTransitionActor.AssignedTechnician => actor.Role == UserRole.Technician && job.IsAssignedTo(actor.UserId),
        _ => false,
    };
}
