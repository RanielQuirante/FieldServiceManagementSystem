using System.Windows.Input;

namespace FieldService.Mobile.Shared.Controls;

public partial class FormField : ContentView, IReleasesInput
{
    public const string ShowPasswordDescription = "Show password";
    public const string HidePasswordDescription = "Hide password";

    private static readonly Thickness _leadingInset = new(16, 0, 0, 0);

    private IKeepInViewHost? _viewHost;

    public const string NoCharactersLeft = "No characters left";
    public const string OptionalMarker = " Optional";
    public const string ReadingFontFamily = "GeistMonoMedium";

    public static readonly BindableProperty LabelProperty = BindableProperty.Create(
        nameof(Label),
        typeof(string),
        typeof(FormField),
        propertyChanged: (bindable, _, _) => ((FormField)bindable).UpdateDescription());

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(FormField),
        string.Empty,
        BindingMode.TwoWay,
        propertyChanged: (bindable, _, _) => ((FormField)bindable).UpdateCount());

    public static readonly BindableProperty IsMultilineProperty = BindableProperty.Create(
        nameof(IsMultiline),
        typeof(bool),
        typeof(FormField),
        false,
        propertyChanged: (bindable, _, newValue) => ((FormField)bindable).IsSingleLine = !(bool)newValue);

    public static readonly BindableProperty IsSingleLineProperty = BindableProperty.Create(nameof(IsSingleLine), typeof(bool), typeof(FormField), true);

    public static readonly BindableProperty HelpTextProperty = BindableProperty.Create(
        nameof(HelpText),
        typeof(string),
        typeof(FormField),
        propertyChanged: (bindable, _, newValue) =>
        {
            var field = (FormField)bindable;
            field.HasHelpText = !string.IsNullOrWhiteSpace(newValue as string);
            field.UpdateDescription();
        });

    public static readonly BindableProperty HasHelpTextProperty = BindableProperty.Create(nameof(HasHelpText), typeof(bool), typeof(FormField), false);

    public static readonly BindableProperty CountFromProperty = BindableProperty.Create(
        nameof(CountFrom),
        typeof(int),
        typeof(FormField),
        0,
        propertyChanged: (bindable, _, _) => ((FormField)bindable).UpdateCount());

    public static readonly BindableProperty CountTextProperty = BindableProperty.Create(nameof(CountText), typeof(string), typeof(FormField));

    public static readonly BindableProperty HasCountTextProperty = BindableProperty.Create(nameof(HasCountText), typeof(bool), typeof(FormField), false);

    public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(nameof(IsReadOnly), typeof(bool), typeof(FormField), false);

    public static readonly BindableProperty InputDescriptionProperty = BindableProperty.Create(nameof(InputDescription), typeof(string), typeof(FormField));

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

    public static readonly BindableProperty MaxLengthProperty = BindableProperty.Create(
        nameof(MaxLength),
        typeof(int),
        typeof(FormField),
        int.MaxValue,
        propertyChanged: (bindable, _, _) => ((FormField)bindable).UpdateCount());

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

    public static readonly BindableProperty IsOptionalProperty = BindableProperty.Create(
        nameof(IsOptional),
        typeof(bool),
        typeof(FormField),
        false,
        propertyChanged: (bindable, _, _) => ((FormField)bindable).UpdateDescription());

    public static readonly BindableProperty OptionalTextProperty = BindableProperty.Create(nameof(OptionalText), typeof(string), typeof(FormField), string.Empty);

    public static readonly BindableProperty UnitProperty = BindableProperty.Create(
        nameof(Unit),
        typeof(string),
        typeof(FormField),
        propertyChanged: (bindable, _, _) => ((FormField)bindable).UpdateLayoutState());

    public static readonly BindableProperty IsReadingProperty = BindableProperty.Create(nameof(IsReading), typeof(bool), typeof(FormField), false);

    public static readonly BindableProperty UnfocusedCommandProperty = BindableProperty.Create(nameof(UnfocusedCommand), typeof(ICommand), typeof(FormField));

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
        Input.Unfocused += (_, _) =>
        {
            UpdateLook();
            RunUnfocused();
        };
        Area.Focused += (_, _) =>
        {
            UpdateLook();
            BringIntoView(keep: true);
        };
        Area.Unfocused += (_, _) =>
        {
            UpdateLook();
            BringIntoView(keep: false);
            RunUnfocused();
        };
        UpdateLayoutState();
        UpdateDescription();
    }

    public bool IsMultiline
    {
        get => (bool)GetValue(IsMultilineProperty);
        set => SetValue(IsMultilineProperty, value);
    }

    public bool IsSingleLine
    {
        get => (bool)GetValue(IsSingleLineProperty);
        private set => SetValue(IsSingleLineProperty, value);
    }

    public string? HelpText
    {
        get => (string?)GetValue(HelpTextProperty);
        set => SetValue(HelpTextProperty, value);
    }

    public bool HasHelpText
    {
        get => (bool)GetValue(HasHelpTextProperty);
        private set => SetValue(HasHelpTextProperty, value);
    }

    public int CountFrom
    {
        get => (int)GetValue(CountFromProperty);
        set => SetValue(CountFromProperty, value);
    }

    public string? CountText
    {
        get => (string?)GetValue(CountTextProperty);
        private set => SetValue(CountTextProperty, value);
    }

    public bool HasCountText
    {
        get => (bool)GetValue(HasCountTextProperty);
        private set => SetValue(HasCountTextProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public string? InputDescription
    {
        get => (string?)GetValue(InputDescriptionProperty);
        private set => SetValue(InputDescriptionProperty, value);
    }

    public static string? RemainingText(int length, int maxLength, int countFrom)
    {
        if (countFrom <= 0 || length < countFrom)
        {
            return null;
        }

        var remaining = Math.Max(0, maxLength - length);
        return remaining switch
        {
            0 => NoCharactersLeft,
            1 => "1 character left",
            _ => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{remaining} characters left"),
        };
    }

    public static bool AnnouncesCount(string? previous, string? current) =>
        current is not null && (previous is null || (current == NoCharactersLeft && previous != NoCharactersLeft));

    public static string Describe(string? label, string? help, string? count)
    {
        var parts = new[] { label, help, count }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .ToList();
        return string.Join(' ', parts.Select((part, index) => index < parts.Count - 1 && !part.EndsWith('.') ? part + "." : part));
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

    public bool IsOptional
    {
        get => (bool)GetValue(IsOptionalProperty);
        set => SetValue(IsOptionalProperty, value);
    }

    public string OptionalText
    {
        get => (string)GetValue(OptionalTextProperty);
        private set => SetValue(OptionalTextProperty, value);
    }

    public string? Unit
    {
        get => (string?)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public bool IsReading
    {
        get => (bool)GetValue(IsReadingProperty);
        private set => SetValue(IsReadingProperty, value);
    }

    public ICommand? UnfocusedCommand
    {
        get => (ICommand?)GetValue(UnfocusedCommandProperty);
        set => SetValue(UnfocusedCommandProperty, value);
    }

    private void RunUnfocused()
    {
        if (UnfocusedCommand is { } command && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    public static string? FieldName(string? label, string? unit, bool isOptional)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return label;
        }

        var name = string.IsNullOrWhiteSpace(unit) ? label.Trim() : label.Trim() + " in " + unit.Trim();
        return isOptional ? name + ", optional" : name;
    }

    public bool HoldsInput => (Input?.IsFocused ?? false) || (Area?.IsFocused ?? false);

    public void ReleaseInput()
    {
        foreach (var field in new InputView?[] { Input, Area })
        {
            if (field is { IsFocused: true })
            {
                _ = field.HideSoftInputAsync(CancellationToken.None);
                field.Unfocus();
            }
        }
    }

    public static bool HidesTextAfterRequest(bool isPassword, int previous, int requested, bool isTextHidden) =>
        requested != previous ? isPassword : isTextHidden;

    private void OnHidePasswordRequested(int previous, int requested) =>
        SetPasswordHidden(HidesTextAfterRequest(IsPassword, previous, requested, IsTextHidden));

    private void OnFocusRequested(int previous, int requested)
    {
        if (requested == previous || requested == 0 || Input is null)
        {
            return;
        }

        if (IsMultiline)
        {
            Area.Focus();
        }
        else
        {
            Input.Focus();
        }
    }

    private void UpdateCount()
    {
        var previous = CountText;
        var current = RemainingText(Text?.Length ?? 0, MaxLength, CountFrom);
        CountText = current;
        HasCountText = current is not null;
        UpdateDescription();
        if (current is not null && AnnouncesCount(previous, current) && Handler is not null)
        {
            SemanticScreenReader.Default.Announce(current);
        }
    }

    private void UpdateDescription()
    {
        OptionalText = IsOptional ? OptionalMarker : string.Empty;
        InputDescription = Describe(FieldName(Label, Unit, IsOptional), HelpText, CountText);
    }

    private void BringIntoView(bool keep)
    {
        if (_viewHost is null)
        {
            Element? ancestor = Parent;
            while (ancestor is not null and not IKeepInViewHost)
            {
                ancestor = ancestor.Parent;
            }

            _viewHost = ancestor as IKeepInViewHost;
        }

        _viewHost?.KeepInView(keep ? this : null);
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
        IsReading = !string.IsNullOrWhiteSpace(Unit);
        EntryMargin = new Thickness(HasIcon ? 0 : _leadingInset.Left, 0, IsPassword || IsReading ? 0 : _leadingInset.Left, 0);
        UpdateDescription();
        if (Stack is null)
        {
            return;
        }

        Stack.Spacing = IsReading ? 6 : 8;
        FieldLabel.SetDynamicResource(StyleProperty, IsReading ? "FsTextReadingLabel" : "FsTextFieldLabel");
        if (IsReading)
        {
            Input.FontFamily = ReadingFontFamily;
        }
        else
        {
            Input.ClearValue(Entry.FontFamilyProperty);
        }
    }

    private void UpdateLook()
    {
        var focused = (Input is not null && Input.IsFocused) || (Area is not null && Area.IsFocused);
        ShowsFocusLook = focused;
        ShowsErrorLook = HasError && !focused;
    }
}
