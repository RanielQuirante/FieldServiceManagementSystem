namespace FieldService.Api.Domain;

public sealed record JobCapabilities(
    IReadOnlyList<StatusChangeOption> AllowedStatusChanges,
    SubmitCapability CanSubmit,
    bool CanAssign,
    bool CanCancel)
{
    public static JobCapabilities For(Job job, JobActor actor, InspectionCompleteness completeness)
    {
        var available = JobTransitions.Available(job, actor).ToList();
        IReadOnlyList<StatusChangeOption> statusChanges =
        [
            .. available
                .Where(transition => transition.Command == JobCommand.ChangeStatus)
                .Select(transition => new StatusChangeOption(transition.To, transition.RequiresNote)),
        ];

        return new JobCapabilities(
            statusChanges,
            SubmitFor(actor, available, completeness),
            available.Exists(transition => transition.Command == JobCommand.Assign),
            available.Exists(transition => transition.Command == JobCommand.Cancel));
    }

    private static SubmitCapability SubmitFor(
        JobActor actor,
        List<JobTransition> available,
        InspectionCompleteness completeness)
    {
        string? blockingCode;
        if (actor.Role != UserRole.Technician)
        {
            blockingCode = DomainErrors.ForbiddenCode;
        }
        else if (!available.Exists(transition => transition.Command == JobCommand.Submit))
        {
            blockingCode = DomainErrors.InvalidTransitionCode;
        }
        else if (!completeness.IsComplete)
        {
            blockingCode = DomainErrors.InspectionIncompleteCode;
        }
        else
        {
            blockingCode = null;
        }

        return new SubmitCapability(blockingCode is null, blockingCode, completeness.Answered, completeness.Total);
    }
}
