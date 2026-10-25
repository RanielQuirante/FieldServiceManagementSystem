namespace FieldService.Mobile.Shared.Controls;

public sealed class OptionGroup : VerticalStackLayout
{
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(OptionGroup));

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public IEnumerable<OptionRow> Rows => Children.OfType<OptionRow>();

    public OptionRow? SelectedRow => Rows.FirstOrDefault(row => row.IsSelected && row.IsAvailable);
}
