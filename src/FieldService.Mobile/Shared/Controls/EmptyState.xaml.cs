using System.Windows.Input;

namespace FieldService.Mobile.Shared.Controls;

public partial class EmptyState : ContentView
{
    public static readonly BindableProperty IconDataProperty = BindableProperty.Create(nameof(IconData), typeof(string), typeof(EmptyState));

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(EmptyState));

    public static readonly BindableProperty MessageProperty = BindableProperty.Create(nameof(Message), typeof(string), typeof(EmptyState));

    public static readonly BindableProperty ActionTextProperty = BindableProperty.Create(
        nameof(ActionText),
        typeof(string),
        typeof(EmptyState),
        propertyChanged: (bindable, _, newValue) => ((EmptyState)bindable).HasAction = !string.IsNullOrEmpty(newValue as string));

    public static readonly BindableProperty ActionDescriptionProperty = BindableProperty.Create(nameof(ActionDescription), typeof(string), typeof(EmptyState));

    public static readonly BindableProperty ActionCommandProperty = BindableProperty.Create(nameof(ActionCommand), typeof(ICommand), typeof(EmptyState));

    public static readonly BindableProperty HasActionProperty = BindableProperty.Create(nameof(HasAction), typeof(bool), typeof(EmptyState), false);

    public EmptyState()
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

    public string? Message
    {
        get => (string?)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string? ActionText
    {
        get => (string?)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public string? ActionDescription
    {
        get => (string?)GetValue(ActionDescriptionProperty);
        set => SetValue(ActionDescriptionProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    public bool HasAction
    {
        get => (bool)GetValue(HasActionProperty);
        private set => SetValue(HasActionProperty, value);
    }
}
