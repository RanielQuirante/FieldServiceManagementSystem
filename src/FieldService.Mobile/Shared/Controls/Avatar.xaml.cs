namespace FieldService.Mobile.Shared.Controls;

public partial class Avatar : ContentView
{
    public static readonly BindableProperty InitialsProperty = BindableProperty.Create(nameof(Initials), typeof(string), typeof(Avatar));

    public Avatar()
    {
        InitializeComponent();
    }

    public string? Initials
    {
        get => (string?)GetValue(InitialsProperty);
        set => SetValue(InitialsProperty, value);
    }
}
