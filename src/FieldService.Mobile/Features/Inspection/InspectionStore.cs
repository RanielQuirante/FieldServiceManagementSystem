using FieldService.Mobile.Shared.Session;

namespace FieldService.Mobile.Features.Inspection;

public sealed record DraftAnswer(InspectionResult? Result, string Note, IReadOnlyDictionary<Guid, string> Readings)
{
    public bool IsAnswer => Result is not null;

    public override string ToString() => $"{nameof(DraftAnswer)} {Result}";
}

public sealed record LoadedInspection(InspectionResponse Inspection, DateTimeOffset LoadedAt);

public sealed record InspectionJob(Guid JobId, long Number, string Title, string TemplateName)
{
    public override string ToString() => $"{nameof(InspectionJob)} {JobId}";
}

public sealed class InspectionStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Dictionary<Guid, DraftAnswer>> _answers = [];
    private readonly Dictionary<Guid, string> _notes = [];
    private readonly Dictionary<Guid, LoadedInspection> _loaded = [];
    private readonly Dictionary<Guid, InspectionJob> _jobs = [];

    public InspectionStore(ISessionService session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.SessionEnded += (_, _) => ClearAll();
    }

    public event EventHandler<Guid>? Changed;

    public void RememberJob(InspectionJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (_gate)
        {
            _jobs[job.JobId] = job;
        }
    }

    public InspectionJob? JobFor(Guid jobId)
    {
        lock (_gate)
        {
            return _jobs.TryGetValue(jobId, out var job) ? job : null;
        }
    }

    public void Remember(InspectionResponse inspection, DateTimeOffset loadedAt)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        lock (_gate)
        {
            _loaded[inspection.JobId] = new LoadedInspection(inspection, loadedAt);
        }
    }

    public LoadedInspection? Loaded(Guid jobId)
    {
        lock (_gate)
        {
            return _loaded.TryGetValue(jobId, out var loaded) ? loaded : null;
        }
    }

    public IReadOnlyDictionary<Guid, DraftAnswer> Answers(Guid jobId)
    {
        lock (_gate)
        {
            return _answers.TryGetValue(jobId, out var answers) ? new Dictionary<Guid, DraftAnswer>(answers) : new Dictionary<Guid, DraftAnswer>();
        }
    }

    public int UnsavedCount(Guid jobId)
    {
        lock (_gate)
        {
            return _answers.TryGetValue(jobId, out var answers) ? answers.Values.Count(answer => answer.IsAnswer) : 0;
        }
    }

    public void Put(Guid jobId, Guid itemId, DraftAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        lock (_gate)
        {
            if (!_answers.TryGetValue(jobId, out var answers))
            {
                answers = [];
                _answers[jobId] = answers;
            }

            answers[itemId] = answer;
        }

        Changed?.Invoke(this, jobId);
    }

    public void Remove(Guid jobId, Guid itemId)
    {
        bool removed;
        lock (_gate)
        {
            removed = _answers.TryGetValue(jobId, out var answers) && answers.Remove(itemId);
        }

        if (removed)
        {
            Changed?.Invoke(this, jobId);
        }
    }

    public string Notes(Guid jobId)
    {
        lock (_gate)
        {
            return _notes.TryGetValue(jobId, out var notes) ? notes : string.Empty;
        }
    }

    public void SetNotes(Guid jobId, string? notes)
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(notes))
            {
                _notes.Remove(jobId);
            }
            else
            {
                _notes[jobId] = notes;
            }
        }
    }

    public void ClearDraft(Guid jobId)
    {
        lock (_gate)
        {
            _answers.Remove(jobId);
            _notes.Remove(jobId);
        }

        Changed?.Invoke(this, jobId);
    }

    public void ClearAnswers(Guid jobId)
    {
        lock (_gate)
        {
            _answers.Remove(jobId);
        }

        Changed?.Invoke(this, jobId);
    }

    public void ClearAll()
    {
        Guid[] jobs;
        lock (_gate)
        {
            jobs = [.. _answers.Keys];
            _answers.Clear();
            _notes.Clear();
            _loaded.Clear();
            _jobs.Clear();
        }

        foreach (var jobId in jobs)
        {
            Changed?.Invoke(this, jobId);
        }
    }
}
