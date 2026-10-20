namespace FieldService.Api.Domain;

public sealed class Inspection
{
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

    public static Inspection Create(Guid jobId, Guid technicianId, DateTimeOffset now)
    {
        return new Inspection
        {
            Id = Guid.CreateVersion7(now),
            JobId = jobId,
            TechnicianId = technicianId,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
