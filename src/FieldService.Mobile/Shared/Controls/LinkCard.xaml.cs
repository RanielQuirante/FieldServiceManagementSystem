using System.Windows.Input;

namespace FieldService.Mobile.Shared.Controls;

public partial class LinkCard : ContentView
{
    public static readonly BindableProperty IconDataProperty = BindableProperty.Create(nameof(IconData), typeof(string), typeof(LinkCard));

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(LinkCard));

    public static readonly BindableProperty SubtitleProperty = BindableProperty.Create(nameof(Subtitle), typeof(string), typeof(LinkCard));

    public static readonly BindableProperty LinkDescriptionProperty = BindableProperty.Create(nameof(LinkDescription), typeof(string), typeof(LinkCard));

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(LinkCard));

    public LinkCard()
    {
        InitializeComponent();
    }

    public string? IconData
    {
        get => (string?)GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Subtitle
    {
        get => (string?)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public string? LinkDescription
    {
        get => (string?)GetValue(LinkDescriptionProperty);
        set => SetValue(LinkDescriptionProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }
}
