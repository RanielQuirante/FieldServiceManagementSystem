namespace FieldService.Api.Domain;

public sealed class InspectionAnswer
{
    private readonly List<InspectionReading> _readings = [];

    private InspectionAnswer()
    {
    }

    public Guid InspectionId { get; private set; }

    public Guid ChecklistItemId { get; private set; }

    public InspectionResult Result { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset AnsweredAt { get; private set; }

    public IReadOnlyList<InspectionReading> Readings => _readings;

    internal static InspectionAnswer Create(Guid inspectionId, ChecklistAnswer answer, DateTimeOffset answeredAt)
    {
        var created = new InspectionAnswer
        {
            InspectionId = inspectionId,
            ChecklistItemId = answer.ChecklistItemId,
            Result = answer.Result,
            Note = DomainText.Optional(answer.Note),
            AnsweredAt = answeredAt,
        };
        created._readings.AddRange(answer.Readings.Select(reading =>
            InspectionReading.Create(inspectionId, answer.ChecklistItemId, reading.ReadingFieldId, reading.Value)));
        return created;
    }

    internal bool Matches(ChecklistAnswer answer) =>
        Result == answer.Result
        && Note == DomainText.Optional(answer.Note)
        && _readings.Count == answer.Readings.Count
        && answer.Readings.All(reading => _readings.Exists(saved =>
            saved.ReadingFieldId == reading.ReadingFieldId && saved.Value == reading.Value));

    internal void Replace(ChecklistAnswer answer, DateTimeOffset answeredAt)
    {
        Result = answer.Result;
        Note = DomainText.Optional(answer.Note);
        AnsweredAt = answeredAt;
        _readings.RemoveAll(saved => !answer.Readings.Any(reading => reading.ReadingFieldId == saved.ReadingFieldId));
        foreach (var reading in answer.Readings)
        {
            var saved = _readings.Find(existing => existing.ReadingFieldId == reading.ReadingFieldId);
            if (saved is null)
            {
                _readings.Add(InspectionReading.Create(InspectionId, ChecklistItemId, reading.ReadingFieldId, reading.Value));
            }
            else
            {
                saved.ChangeValue(reading.Value);
            }
        }
    }
}
