using System.Windows.Input;

namespace FieldService.Mobile.Shared.Controls;

public partial class FormField : ContentView
{
    public const string ShowPasswordDescription = "Show password";
    public const string HidePasswordDescription = "Hide password";

    private static readonly Thickness _leadingInset = new(16, 0, 0, 0);

    public static readonly BindableProperty LabelProperty = BindableProperty.Create(nameof(Label), typeof(string), typeof(FormField));

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(FormField),
        string.Empty,
        BindingMode.TwoWay);

    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(FormField));

    public static readonly BindableProperty IconDataProperty = BindableProperty.Create(
        nameof(IconData),
        typeof(string),
        typeof(FormField),
        propertyChanged: (bindable, _, _) => ((FormField)bindable).UpdateLayoutState());

    public static readonly BindableProperty IsPasswordProperty = BindableProperty.Create(
        nameof(IsPassword),
        typeof(bool),
        typeof(FormField),
        false,
        propertyChanged: (bindable, _, newValue) => ((FormField)bindable).ResetPasswordVisibility((bool)newValue));

    public static readonly BindableProperty ErrorTextProperty = BindableProperty.Create(
        nameof(ErrorText),
        typeof(string),
        typeof(FormField),
        propertyChanged: (bindable, _, newValue) => ((FormField)bindable).OnErrorTextChanged(newValue as string));

    public static readonly BindableProperty KeyboardProperty = BindableProperty.Create(nameof(Keyboard), typeof(Keyboard), typeof(FormField), Keyboard.Default);

    public static readonly BindableProperty ReturnTypeProperty = BindableProperty.Create(nameof(ReturnType), typeof(ReturnType), typeof(FormField), ReturnType.Default);

    public static readonly BindableProperty ReturnCommandProperty = BindableProperty.Create(nameof(ReturnCommand), typeof(ICommand), typeof(FormField));

    public static readonly BindableProperty MaxLengthProperty = BindableProperty.Create(nameof(MaxLength), typeof(int), typeof(FormField), int.MaxValue);

    public static readonly BindableProperty IsSpellCheckEnabledProperty = BindableProperty.Create(nameof(IsSpellCheckEnabled), typeof(bool), typeof(FormField), true);

    public static readonly BindableProperty IsTextPredictionEnabledProperty = BindableProperty.Create(nameof(IsTextPredictionEnabled), typeof(bool), typeof(FormField), true);

    public static readonly BindableProperty IsTextHiddenProperty = BindableProperty.Create(nameof(IsTextHidden), typeof(bool), typeof(FormField), false);

    public static readonly BindableProperty TogglePasswordDescriptionProperty = BindableProperty.Create(
        nameof(TogglePasswordDescription),
        typeof(string),
        typeof(FormField),
        ShowPasswordDescription);

    public static readonly BindableProperty HasIconProperty = BindableProperty.Create(nameof(HasIcon), typeof(bool), typeof(FormField), false);

    public static readonly BindableProperty HasErrorProperty = BindableProperty.Create(nameof(HasError), typeof(bool), typeof(FormField), false);

    public static readonly BindableProperty ShowsFocusLookProperty = BindableProperty.Create(nameof(ShowsFocusLook), typeof(bool), typeof(FormField), false);

    public static readonly BindableProperty ShowsErrorLookProperty = BindableProperty.Create(nameof(ShowsErrorLook), typeof(bool), typeof(FormField), false);

    public static readonly BindableProperty EntryMarginProperty = BindableProperty.Create(nameof(EntryMargin), typeof(Thickness), typeof(FormField), _leadingInset);

    public static readonly BindableProperty FocusRequestProperty = BindableProperty.Create(
        nameof(FocusRequest),
        typeof(int),
        typeof(FormField),
        0,
        propertyChanged: (bindable, oldValue, newValue) => ((FormField)bindable).OnFocusRequested((int)oldValue, (int)newValue));

    public static readonly BindableProperty HidePasswordRequestProperty = BindableProperty.Create(
        nameof(HidePasswordRequest),
        typeof(int),
        typeof(FormField),
        0,
        propertyChanged: (bindable, oldValue, newValue) => ((FormField)bindable).OnHidePasswordRequested((int)oldValue, (int)newValue));

    public FormField()
    {
        TogglePasswordCommand = new Command(() => SetPasswordHidden(!IsTextHidden));
        InitializeComponent();
        Input.Focused += (_, _) => UpdateLook();
        Input.Unfocused += (_, _) => UpdateLook();
        UpdateLayoutState();
    }

    public ICommand TogglePasswordCommand { get; }

    public string? Label
    {
        get => (string?)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? Placeholder
    {
        get => (string?)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public string? IconData
    {
        get => (string?)GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public bool IsPassword
    {
        get => (bool)GetValue(IsPasswordProperty);
        set => SetValue(IsPasswordProperty, value);
    }

    public string? ErrorText
    {
        get => (string?)GetValue(ErrorTextProperty);
        set => SetValue(ErrorTextProperty, value);
    }

    public Keyboard Keyboard
    {
        get => (Keyboard)GetValue(KeyboardProperty);
        set => SetValue(KeyboardProperty, value);
    }

    public ReturnType ReturnType
    {
        get => (ReturnType)GetValue(ReturnTypeProperty);
        set => SetValue(ReturnTypeProperty, value);
    }

    public ICommand? ReturnCommand
    {
        get => (ICommand?)GetValue(ReturnCommandProperty);
        set => SetValue(ReturnCommandProperty, value);
    }

    public int MaxLength
    {
        get => (int)GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }

    public bool IsSpellCheckEnabled
    {
        get => (bool)GetValue(IsSpellCheckEnabledProperty);
        set => SetValue(IsSpellCheckEnabledProperty, value);
    }

    public bool IsTextPredictionEnabled
    {
        get => (bool)GetValue(IsTextPredictionEnabledProperty);
        set => SetValue(IsTextPredictionEnabledProperty, value);
    }

    public bool IsTextHidden
    {
        get => (bool)GetValue(IsTextHiddenProperty);
        private set => SetValue(IsTextHiddenProperty, value);
    }

    public string TogglePasswordDescription
    {
        get => (string)GetValue(TogglePasswordDescriptionProperty);
        private set => SetValue(TogglePasswordDescriptionProperty, value);
    }

    public bool HasIcon
    {
        get => (bool)GetValue(HasIconProperty);
        private set => SetValue(HasIconProperty, value);
    }

    public bool HasError
    {
        get => (bool)GetValue(HasErrorProperty);
        private set => SetValue(HasErrorProperty, value);
    }

    public bool ShowsFocusLook
    {
        get => (bool)GetValue(ShowsFocusLookProperty);
        private set => SetValue(ShowsFocusLookProperty, value);
    }

    public bool ShowsErrorLook
    {
        get => (bool)GetValue(ShowsErrorLookProperty);
        private set => SetValue(ShowsErrorLookProperty, value);
    }

    public Thickness EntryMargin
    {
        get => (Thickness)GetValue(EntryMarginProperty);
        private set => SetValue(EntryMarginProperty, value);
    }

    public int FocusRequest
    {
        get => (int)GetValue(FocusRequestProperty);
        set => SetValue(FocusRequestProperty, value);
    }

    public int HidePasswordRequest
    {
        get => (int)GetValue(HidePasswordRequestProperty);
        set => SetValue(HidePasswordRequestProperty, value);
    }

    public static bool HidesTextAfterRequest(bool isPassword, int previous, int requested, bool isTextHidden) =>
        requested != previous ? isPassword : isTextHidden;

    private void OnHidePasswordRequested(int previous, int requested) =>
        SetPasswordHidden(HidesTextAfterRequest(IsPassword, previous, requested, IsTextHidden));

    private void OnFocusRequested(int previous, int requested)
    {
        if (requested != previous && requested != 0 && Input is not null)
        {
            Input.Focus();
        }
    }

    private void ResetPasswordVisibility(bool isPassword)
    {
        SetPasswordHidden(isPassword);
        UpdateLayoutState();
    }

    private void SetPasswordHidden(bool hidden)
    {
        IsTextHidden = hidden && IsPassword;
        TogglePasswordDescription = IsTextHidden ? ShowPasswordDescription : HidePasswordDescription;
    }

    private void OnErrorTextChanged(string? errorText)
    {
        HasError = !string.IsNullOrEmpty(errorText);
        UpdateLook();
        if (errorText is { Length: > 0 } && Handler is not null)
        {
            SemanticScreenReader.Default.Announce(errorText);
        }
    }

    private void UpdateLayoutState()
    {
        HasIcon = !string.IsNullOrWhiteSpace(IconData);
        EntryMargin = new Thickness(HasIcon ? 0 : _leadingInset.Left, 0, IsPassword ? 0 : _leadingInset.Left, 0);
    }

    private void UpdateLook()
    {
        var focused = Input is not null && Input.IsFocused;
        ShowsFocusLook = focused;
        ShowsErrorLook = HasError && !focused;
    }
}
