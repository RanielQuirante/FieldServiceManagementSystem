using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FieldService.Mobile.Features.Inspection;

public interface IInspectionItemHost
{
    bool AcceptsInput { get; }

    void OnItemEdited(InspectionItemViewModel item);

    void OnResultSelected(InspectionItemViewModel item, InspectionResult result);
}

public sealed partial class InspectionItemViewModel : ObservableObject
{
    public const string FaultLabel = "Describe the fault";
    public const string NoteLabel = "Note";
    public const string FaultRequiredMessage = "Describe the fault to fail this item.";
    public const string NotAnsweredText = "Not answered";
    public const int NoteMaxLength = 1000;
    public const int NoteCountFrom = 900;

    private readonly IInspectionItemHost _host;
    private bool _applying;

    public InspectionItemViewModel(IInspectionItemHost host, InspectionItemResponse item, int total)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(item);
        _host = host;
        Item = item;
        Total = total;
        Readings = [.. item.ReadingFields.OrderBy(field => field.Position).Select(field => new ReadingFieldViewModel(this, field))];
        Saved = item.Answer;
        ApplyAnswer(item.Answer);
    }

    public InspectionItemResponse Item { get; private set; }

    public Guid ChecklistItemId => Item.ChecklistItemId;

    public int Position => Item.Position;

    public int Total { get; }

    public string Prompt => Item.Prompt;

    public string NumberText => Number(Position, Total) + " / " + Number(Total, Total);

    public string ShortNumber => Number(Position, Total);

    public string GroupDescription => string.Create(CultureInfo.InvariantCulture, $"Item {Position} of {Total}, {Prompt}");

    public IReadOnlyList<ReadingFieldViewModel> Readings { get; }

    public bool HasReadingFields => Readings.Count > 0;

    public InspectionAnswerResponse? Saved { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsPass),
        nameof(IsFail),
        nameof(IsNotApplicable),
        nameof(IsAnswered),
        nameof(ShowsReadings),
        nameof(ShowsEditableReadings),
        nameof(ShowsFaultField),
        nameof(CanAddNote),
        nameof(ShowsNoteBox),
        nameof(ShowsNoteField))]
    public partial InspectionResult? Result { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote), nameof(ShowsNoteBox), nameof(NoteButtonDescription))]
    public partial string Note { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsNoteBox), nameof(ShowsNoteField))]
    public partial bool IsEditingNote { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsFaultField), nameof(CanAddNote), nameof(ShowsNoteField), nameof(IsReadOnly), nameof(ShowsResultChip), nameof(ShowsNotAnswered), nameof(ShowsNoteBox), nameof(CardDescription), nameof(ShowsEditableReadings), nameof(ShowsSavedReadings))]
    public partial bool IsEditable { get; set; } = true;

    [ObservableProperty]
    public partial string? FaultError { get; private set; }

    [ObservableProperty]
    public partial string? NoteError { get; private set; }

    [ObservableProperty]
    public partial bool ShowsErrors { get; private set; }

    [ObservableProperty]
    public partial int FaultFocusRequest { get; private set; }

    [ObservableProperty]
    public partial int NoteFocusRequest { get; private set; }

    [ObservableProperty]
    public partial int PromptFocusRequest { get; private set; }

    [ObservableProperty]
    public partial int ScrollRequest { get; private set; }

    public bool IsPass => Result == InspectionResult.Pass;

    public bool IsFail => Result == InspectionResult.Fail;

    public bool IsNotApplicable => Result == InspectionResult.NotApplicable;

    public bool IsAnswered => Result is not null;

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    public bool ShowsReadings => HasReadingFields && !IsNotApplicable;

    public bool ShowsEditableReadings => ShowsReadings && IsEditable;

    public string? CardDescription => IsReadOnly ? ReadOnlyDescription : null;

    public bool ShowsFaultField => IsEditable && IsFail;

    public bool CanAddNote => IsEditable && !IsFail;

    public bool ShowsNoteBox => !IsFail && HasNote && !(IsEditable && IsEditingNote);

    public bool ShowsNoteField => IsEditable && !IsFail && IsEditingNote;

    public string NoteButtonDescription => (HasNote ? "Edit note for item " : "Add note to item ") + ShortNumber;

    public bool IsReadOnly => !IsEditable;

    public bool ShowsResultChip => IsReadOnly && Saved is not null;

    public bool ShowsNotAnswered => IsReadOnly && Saved is null;

    public InspectionResult SavedResult => Saved?.Result ?? InspectionResult.Unknown;

    public string? SavedNote => string.IsNullOrWhiteSpace(Saved?.Note) ? null : Saved!.Note!.Trim();

    public bool HasSavedNote => SavedNote is not null;

    public IReadOnlyList<SavedReading> SavedReadings => Saved is null
        ? []
        : [.. Readings
            .Select(definition => (definition, value: Saved.Readings.FirstOrDefault(reading => reading.ReadingFieldId == definition.ReadingFieldId)))
            .Where(pair => pair.value is not null)
            .Select(pair => new SavedReading(pair.definition.Label, ReadingValue.WithUnit(pair.value!.Value, pair.definition.Unit)))];

    public bool ShowsSavedReadings => IsReadOnly && SavedReadings.Count > 0;

    public string ReadOnlyDescription
    {
        get
        {
            var parts = new List<string> { GroupDescription, Saved is null ? NotAnsweredText : ResultLabel(Saved.Result) };
            parts.AddRange(SavedReadings.Select(reading => reading.Label + " " + reading.Value));
            if (SavedNote is { } note)
            {
                parts.Add(note);
            }

            return string.Join(", ", parts);
        }
    }

    public bool IsComplete => Result switch
    {
        InspectionResult.Pass => Readings.All(reading => reading.Parsed.IsValid),
        InspectionResult.Fail => HasNote && Readings.All(reading => reading.IsEmpty || reading.Parsed.IsValid),
        InspectionResult.NotApplicable => true,
        _ => false,
    };

    public bool IsIncompleteAnswer => IsAnswered && !IsComplete;

    public bool IsChanged => !string.Equals(Signature(), SavedSignature(Saved), StringComparison.Ordinal)
        || (Result is null && (HasNote || Readings.Any(reading => !reading.IsEmpty)));

    public static string ResultLabel(InspectionResult result) => result switch
    {
        InspectionResult.Pass => "Pass",
        InspectionResult.Fail => "Fail",
        InspectionResult.NotApplicable => "N/A",
        _ => "Not answered",
    };

    public static string Number(int value, int total)
    {
        var width = Math.Max(2, total.ToString(CultureInfo.InvariantCulture).Length);
        return value.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
    }

    public InspectionAnswerInput ToInput()
    {
        var result = Result ?? InspectionResult.Unknown;
        IReadOnlyList<InspectionReadingInput>? readings = result switch
        {
            InspectionResult.Pass => [.. Readings.Select(reading => new InspectionReadingInput(reading.ReadingFieldId, reading.Parsed.Value ?? 0))],
            InspectionResult.Fail => [.. Readings.Where(reading => !reading.IsEmpty).Select(reading => new InspectionReadingInput(reading.ReadingFieldId, reading.Parsed.Value ?? 0))],
            _ => null,
        };

        return new InspectionAnswerInput(
            ChecklistItemId,
            result,
            HasNote ? Note.Trim() : null,
            readings is { Count: > 0 } ? readings : null);
    }

    public IReadOnlyList<ReadingFieldViewModel> SentReadings() => Result switch
    {
        InspectionResult.Pass => Readings,
        InspectionResult.Fail => [.. Readings.Where(reading => !reading.IsEmpty)],
        _ => [],
    };

    public DraftAnswer ToDraft() => new(
        Result,
        Note,
        Readings.Where(reading => !reading.IsEmpty).ToDictionary(reading => reading.ReadingFieldId, reading => reading.Text));

    public void ApplyDraft(DraftAnswer draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        _applying = true;
        try
        {
            Result = draft.Result ?? Result;
            Note = draft.Note;
            foreach (var reading in Readings)
            {
                reading.SetText(draft.Readings.TryGetValue(reading.ReadingFieldId, out var text) ? text : string.Empty);
            }
        }
        finally
        {
            _applying = false;
        }

        if (IsIncompleteAnswer)
        {
            RevealErrors();
        }
    }

    public void ApplySaved(InspectionItemResponse item, bool keepEdits)
    {
        ArgumentNullException.ThrowIfNull(item);
        Item = item;
        Saved = item.Answer;
        if (!keepEdits)
        {
            ApplyAnswer(item.Answer);
            ShowsErrors = false;
            ClearErrors();
        }

        NotifySaved();
    }

    public void RevealErrors()
    {
        ShowsErrors = true;
        UpdateErrors();
    }

    public void HideErrors()
    {
        ShowsErrors = false;
        ClearErrors();
    }

    public void ShowServerNoteError(string message)
    {
        if (IsFail)
        {
            FaultError = message;
            return;
        }

        IsEditingNote = true;
        NoteError = message;
    }

    public void FocusFirstError()
    {
        ScrollRequest++;
        if (FaultError is not null && IsFail)
        {
            FaultFocusRequest++;
            return;
        }

        if (NoteError is not null)
        {
            NoteFocusRequest++;
            return;
        }

        var reading = Readings.FirstOrDefault(candidate => candidate.Error is not null);
        if (reading is not null)
        {
            reading.RequestFocus();
            return;
        }

        PromptFocusRequest++;
    }

    public void FocusPrompt()
    {
        ScrollRequest++;
        PromptFocusRequest++;
    }

    public void OnReadingEdited()
    {
        if (_applying)
        {
            return;
        }

        UpdateErrors();
        _host.OnItemEdited(this);
    }

    [RelayCommand]
    private void Select(InspectionResult result)
    {
        if (!IsEditable || !_host.AcceptsInput || Result == result || result == InspectionResult.Unknown)
        {
            return;
        }

        Result = result;
        UpdateErrors();
        if (result == InspectionResult.Pass && Readings.FirstOrDefault(reading => !reading.Parsed.IsValid) is { } missing)
        {
            missing.RequestFocus();
        }
        else if (result == InspectionResult.Fail)
        {
            FaultFocusRequest++;
        }

        _host.OnItemEdited(this);
        _host.OnResultSelected(this, result);
    }

    [RelayCommand]
    private void BeginNote()
    {
        if (!CanAddNote || !_host.AcceptsInput)
        {
            return;
        }

        IsEditingNote = true;
        NoteFocusRequest++;
    }

    [RelayCommand]
    private void EndNote() => IsEditingNote = false;

    partial void OnNoteChanged(string value)
    {
        if (_applying)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            NoteError = null;
            FaultError = null;
        }

        UpdateErrors();
        _host.OnItemEdited(this);
    }

    private void ApplyAnswer(InspectionAnswerResponse? answer)
    {
        _applying = true;
        try
        {
            Result = answer?.Result is InspectionResult.Pass or InspectionResult.Fail or InspectionResult.NotApplicable ? answer.Result : null;
            Note = answer?.Note ?? string.Empty;
            foreach (var reading in Readings)
            {
                var saved = answer?.Readings.FirstOrDefault(candidate => candidate.ReadingFieldId == reading.ReadingFieldId);
                reading.SetText(saved is null ? string.Empty : ReadingValue.Format(saved.Value));
            }

            IsEditingNote = false;
        }
        finally
        {
            _applying = false;
        }
    }

    private void UpdateErrors()
    {
        if (!ShowsErrors)
        {
            return;
        }

        if (FaultError is null || !string.IsNullOrWhiteSpace(Note) || !IsFail)
        {
            FaultError = IsFail && !HasNote ? FaultRequiredMessage : null;
        }

        foreach (var reading in Readings)
        {
            reading.ShowError(Result switch
            {
                InspectionResult.Pass => ReadingValue.Message(reading.Parsed.Problem, requiredForPass: true),
                InspectionResult.Fail => ReadingValue.Message(reading.Parsed.Problem, requiredForPass: false),
                _ => null,
            });
        }
    }

    private void ClearErrors()
    {
        FaultError = null;
        NoteError = null;
        foreach (var reading in Readings)
        {
            reading.ClearErrors();
        }
    }

    private void NotifySaved()
    {
        OnPropertyChanged(nameof(Saved));
        OnPropertyChanged(nameof(SavedResult));
        OnPropertyChanged(nameof(SavedNote));
        OnPropertyChanged(nameof(HasSavedNote));
        OnPropertyChanged(nameof(SavedReadings));
        OnPropertyChanged(nameof(ShowsSavedReadings));
        OnPropertyChanged(nameof(ReadOnlyDescription));
        OnPropertyChanged(nameof(CardDescription));
        OnPropertyChanged(nameof(ShowsResultChip));
        OnPropertyChanged(nameof(ShowsNotAnswered));
        OnPropertyChanged(nameof(Prompt));
    }

    private string Signature()
    {
        if (Result is not { } result)
        {
            return string.Empty;
        }

        var readings = SentReadings().Select(reading => reading.ReadingFieldId.ToString("N") + "=" + (reading.Parsed.Value is { } value ? ReadingValue.Format(value) : "!" + reading.Text));
        return result + "|" + (HasNote ? Note.Trim() : string.Empty) + "|" + string.Join(";", readings.Order(StringComparer.Ordinal));
    }

    private static string SavedSignature(InspectionAnswerResponse? saved)
    {
        if (saved is null)
        {
            return string.Empty;
        }

        var readings = saved.Result == InspectionResult.NotApplicable
            ? []
            : saved.Readings.Select(reading => reading.ReadingFieldId.ToString("N") + "=" + ReadingValue.Format(reading.Value));
        return saved.Result + "|" + (string.IsNullOrWhiteSpace(saved.Note) ? string.Empty : saved.Note.Trim()) + "|" + string.Join(";", readings.Order(StringComparer.Ordinal));
    }
}

public sealed record SavedReading(string Label, string Value);
