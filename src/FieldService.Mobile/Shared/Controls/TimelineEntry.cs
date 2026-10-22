namespace FieldService.Mobile.Shared.Controls;

public sealed record TimelineEntry(string? Day, string Time, string Text, string? Note, bool IsNewest)
{
    public bool HasDay => !string.IsNullOrEmpty(Day);

    public bool HasNote => !string.IsNullOrEmpty(Note);

    public string QuotedNote => HasNote ? "“" + Note + "”" : string.Empty;

    public string Description
    {
        get
        {
            var moment = HasDay ? Day + ", " + Time : Time;
            var text = moment + ", " + Text;
            return HasNote ? text + ", " + QuotedNote : text;
        }
    }

    public override string ToString() => nameof(TimelineEntry);
}
