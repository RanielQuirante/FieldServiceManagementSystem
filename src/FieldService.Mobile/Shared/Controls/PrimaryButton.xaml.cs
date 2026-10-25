using System.ComponentModel;
using System.Windows.Input;

namespace FieldService.Mobile.Shared.Controls;

public partial class PrimaryButton : ContentView
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(PrimaryButton),
        propertyChanged: (bindable, _, _) => ((PrimaryButton)bindable).UpdateLook());

    public static readonly BindableProperty BusyTextProperty = BindableProperty.Create(
        nameof(BusyText),
        typeof(string),
        typeof(PrimaryButton),
        propertyChanged: (bindable, _, _) => ((PrimaryButton)bindable).UpdateLook());

    public static readonly BindableProperty IsBusyProperty = BindableProperty.Create(
        nameof(IsBusy),
        typeof(bool),
        typeof(PrimaryButton),
        false,
        propertyChanged: (bindable, _, _) => ((PrimaryButton)bindable).UpdateLook());

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(PrimaryButton));

    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(PrimaryButton));

    public static readonly BindableProperty DisplayTextProperty = BindableProperty.Create(nameof(DisplayText), typeof(string), typeof(PrimaryButton));

    public static readonly BindableProperty ShowsDisabledLookProperty = BindableProperty.Create(nameof(ShowsDisabledLook), typeof(bool), typeof(PrimaryButton), false);

    public static readonly BindableProperty IconDataProperty = BindableProperty.Create(
        nameof(IconData),
        typeof(string),
        typeof(PrimaryButton),
        propertyChanged: (bindable, _, _) => ((PrimaryButton)bindable).UpdateLook());

    public static readonly BindableProperty IconKeyProperty = BindableProperty.Create(
        nameof(IconKey),
        typeof(string),
        typeof(PrimaryButton),
        propertyChanged: (bindable, _, newValue) =>
        {
            if (newValue is string { Length: > 0 } key)
            {
                ((PrimaryButton)bindable).SetDynamicResource(IconDataProperty, key);
            }
        });

    public static readonly BindableProperty SemanticFocusRequestProperty = BindableProperty.Create(
        nameof(SemanticFocusRequest),
        typeof(int),
        typeof(PrimaryButton),
        0,
        propertyChanged: (bindable, oldValue, newValue) => ((PrimaryButton)bindable).OnSemanticFocusRequested((int)oldValue, (int)newValue));

    public static readonly BindableProperty HasIconProperty = BindableProperty.Create(nameof(HasIcon), typeof(bool), typeof(PrimaryButton), false);

    public static readonly BindableProperty ButtonTextProperty = BindableProperty.Create(nameof(ButtonText), typeof(string), typeof(PrimaryButton));

    public PrimaryButton()
    {
        InitializeComponent();
        CoreButton.PropertyChanged += OnCoreButtonPropertyChanged;
        UpdateLook();
    }

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? BusyText
    {
        get => (string?)GetValue(BusyTextProperty);
        set => SetValue(BusyTextProperty, value);
    }

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public string? DisplayText
    {
        get => (string?)GetValue(DisplayTextProperty);
        private set => SetValue(DisplayTextProperty, value);
    }

    public bool ShowsDisabledLook
    {
        get => (bool)GetValue(ShowsDisabledLookProperty);
        private set => SetValue(ShowsDisabledLookProperty, value);
    }

    public string? IconData
    {
        get => (string?)GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public string? IconKey
    {
        get => (string?)GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    public int SemanticFocusRequest
    {
        get => (int)GetValue(SemanticFocusRequestProperty);
        set => SetValue(SemanticFocusRequestProperty, value);
    }

    public bool HasIcon
    {
        get => (bool)GetValue(HasIconProperty);
        private set => SetValue(HasIconProperty, value);
    }

    public string? ButtonText
    {
        get => (string?)GetValue(ButtonTextProperty);
        private set => SetValue(ButtonTextProperty, value);
    }

    private void OnSemanticFocusRequested(int previous, int requested)
    {
        if (requested != previous && requested != 0 && CoreButton?.Handler is not null)
        {
            Dispatcher.Dispatch(() => FocusMoves.MoveTo(CoreButton));
        }
    }

    private void OnCoreButtonPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == IsEnabledProperty.PropertyName)
        {
            UpdateLook();
        }
    }

    private void UpdateLook()
    {
        DisplayText = IsBusy && !string.IsNullOrEmpty(BusyText) ? BusyText : Text;
        HasIcon = !string.IsNullOrWhiteSpace(IconData);
        ButtonText = HasIcon ? string.Empty : DisplayText;
        ShowsDisabledLook = CoreButton is not null && !CoreButton.IsEnabled && !IsBusy;
    }
}
