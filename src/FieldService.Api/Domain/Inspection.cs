namespace FieldService.Api.Domain;

public sealed class Inspection
{
    public const int MaxTechnicianNotesLength = 2000;

    private readonly List<InspectionAnswer> _answers = [];

    private Inspection()
    {
    }

    public Guid Id { get; private set; }

    public Guid JobId { get; private set; }

    public Guid TechnicianId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public string? TechnicianNotes { get; private set; }

    public IReadOnlyList<InspectionAnswer> Answers => _answers;

    public bool IsSubmitted => SubmittedAt is not null;

    public static Inspection Start(Job job, DateTimeOffset now)
    {
        if (job.AssignedTechnicianId is not { } technicianId)
        {
            throw new InvalidOperationException("A job without an assigned technician has no inspection.");
        }

        var at = UtcInstant.Truncate(now);
        return new Inspection
        {
            Id = Guid.CreateVersion7(at),
            JobId = job.Id,
            TechnicianId = technicianId,
            CreatedAt = at,
            UpdatedAt = at,
        };
    }

    public static bool CanEdit(Job job, Inspection? inspection, JobActor actor) =>
        EditError(job, inspection, actor) is null;

    public bool HasSameAnswers(IReadOnlyList<ChecklistAnswer> answers) =>
        answers.All(answer => _answers.Find(saved => saved.ChecklistItemId == answer.ChecklistItemId) is { } saved
            && saved.Matches(answer));

    public DomainError? SaveAnswers(
        Job job,
        InspectionChecklist checklist,
        IReadOnlyList<ChecklistAnswer> answers,
        JobActor actor,
        DateTimeOffset now)
    {
        if (job.Id != JobId)
        {
            throw new ArgumentException("The inspection belongs to another job.", nameof(job));
        }

        job.EnsureChecklistOfThisJob(checklist);
        if (EditError(job, this, actor) is { } locked)
        {
            return locked;
        }

        if (InspectionRules.Validate(checklist, answers) is [var first, ..])
        {
            return first;
        }

        var at = UtcInstant.Truncate(now);
        var changed = false;
        foreach (var answer in answers)
        {
            var saved = _answers.Find(existing => existing.ChecklistItemId == answer.ChecklistItemId);
            if (saved is null)
            {
                _answers.Add(InspectionAnswer.Create(Id, answer, at));
                changed = true;
            }
            else if (!saved.Matches(answer))
            {
                saved.Replace(answer, at);
                changed = true;
            }
        }

        if (changed)
        {
            UpdatedAt = at;
            job.TouchForInspection(at);
        }

        return null;
    }

    internal void MarkSubmitted(string? technicianNotes, DateTimeOffset at)
    {
        SubmittedAt = at;
        TechnicianNotes = technicianNotes;
        UpdatedAt = at;
    }

    private static DomainError? EditError(Job job, Inspection? inspection, JobActor actor)
    {
        if (inspection is { IsSubmitted: true })
        {
            return DomainErrors.InspectionSubmitted();
        }

        if (job.Status != JobStatus.InProgress)
        {
            return DomainErrors.InspectionNotEditable(job);
        }

        if (actor.Role != UserRole.Technician || !job.IsAssignedTo(actor.UserId))
        {
            return DomainErrors.InspectionNotOwned();
        }

        return null;
    }
}
