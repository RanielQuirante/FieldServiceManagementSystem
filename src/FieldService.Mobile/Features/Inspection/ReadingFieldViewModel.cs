using CommunityToolkit.Mvvm.ComponentModel;

namespace FieldService.Mobile.Features.Inspection;

public sealed partial class ReadingFieldViewModel : ObservableObject
{
    private readonly InspectionItemViewModel _item;
    private bool _applying;

    public ReadingFieldViewModel(InspectionItemViewModel item, InspectionReadingFieldResponse field)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(field);
        _item = item;
        Field = field;
    }

    public InspectionReadingFieldResponse Field { get; }

    public Guid ReadingFieldId => Field.ReadingFieldId;

    public string Label => Field.Label;

    public string Unit => Field.Unit;

    public string Description => string.IsNullOrWhiteSpace(Unit) ? Label : Label + " in " + Unit;

    public string Placeholder => ReadingValue.Placeholder;

    public int MaxLength => ReadingValue.MaxLength;

    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    public partial string? ServerError { get; private set; }

    [ObservableProperty]
    public partial int FocusRequest { get; private set; }

    public ReadingParse Parsed => ReadingValue.Parse(Text);

    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);

    public void SetText(string text)
    {
        _applying = true;
        try
        {
            Text = text;
        }
        finally
        {
            _applying = false;
        }
    }

    public void ShowServerError(string message)
    {
        ServerError = message;
        Error = message;
    }

    public void ShowError(string? message)
    {
        if (ServerError is null)
        {
            Error = message;
        }
    }

    public void ClearErrors()
    {
        ServerError = null;
        Error = null;
    }

    public void RequestFocus() => FocusRequest++;

    partial void OnTextChanged(string value)
    {
        if (_applying)
        {
            return;
        }

        if (ServerError is not null)
        {
            ServerError = null;
            Error = null;
        }

        _item.OnReadingEdited();
    }
}
