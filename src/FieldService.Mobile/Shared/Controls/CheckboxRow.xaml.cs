namespace FieldService.Mobile.Shared.Controls;

public partial class CheckboxRow : ContentView, IKeyboardRow
{
    public const string AndroidClassName = "android.widget.CheckBox";
    public const double CornerRadius = 22;

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(CheckboxRow),
        propertyChanged: (bindable, _, _) => ((CheckboxRow)bindable).UpdateLook());

    public static readonly BindableProperty IsCheckedProperty = BindableProperty.Create(
        nameof(IsChecked),
        typeof(bool),
        typeof(CheckboxRow),
        false,
        BindingMode.TwoWay,
        propertyChanged: (bindable, _, _) => ((CheckboxRow)bindable).UpdateLook());

    public static readonly BindableProperty ErrorTextProperty = BindableProperty.Create(
        nameof(ErrorText),
        typeof(string),
        typeof(CheckboxRow),
        propertyChanged: (bindable, _, newValue) => ((CheckboxRow)bindable).OnErrorTextChanged(newValue as string));

    public static readonly BindableProperty IsInputBlockedProperty = BindableProperty.Create(nameof(IsInputBlocked), typeof(bool), typeof(CheckboxRow), false);

    public static readonly BindableProperty HasErrorProperty = BindableProperty.Create(nameof(HasError), typeof(bool), typeof(CheckboxRow), false);

    public static readonly BindableProperty FocusRequestProperty = BindableProperty.Create(
        nameof(FocusRequest),
        typeof(int),
        typeof(CheckboxRow),
        0,
        propertyChanged: (bindable, oldValue, newValue) => ((CheckboxRow)bindable).OnFocusRequested((int)oldValue, (int)newValue));

    public CheckboxRow()
    {
        InitializeComponent();
        UpdateLook();
    }

    public static bool ExposesNativeState => OperatingSystem.IsAndroid() || OperatingSystem.IsWindows();

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsChecked
    {
        get => (bool)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    public string? ErrorText
    {
        get => (string?)GetValue(ErrorTextProperty);
        set => SetValue(ErrorTextProperty, value);
    }

    public bool IsInputBlocked
    {
        get => (bool)GetValue(IsInputBlockedProperty);
        set => SetValue(IsInputBlockedProperty, value);
    }

    public bool HasError
    {
        get => (bool)GetValue(HasErrorProperty);
        private set => SetValue(HasErrorProperty, value);
    }

    public int FocusRequest
    {
        get => (int)GetValue(FocusRequestProperty);
        set => SetValue(FocusRequestProperty, value);
    }

    public KeyboardRowRole Role => KeyboardRowRole.CheckBox;

    public string AccessibleName => Text ?? string.Empty;

    public string? AccessibleDetail => HasError ? ErrorText : null;

    public bool IsOn => IsChecked;

    public bool IsOperable => !IsInputBlocked && IsEnabled;

    public double FocusCornerRadius => CornerRadius;

    public Thickness FocusInset => InsetFor(Height, Surface?.Bounds ?? Rect.Zero);

    public static Thickness InsetFor(double rowHeight, Rect card) =>
        card.Height <= 0 || rowHeight <= 0 ? Thickness.Zero : new Thickness(0, 0, 0, Math.Max(0, rowHeight - card.Bottom));

    public bool Operate() => Toggle();

    public static string Describe(string? text, bool isChecked, string? error, bool nativeState)
    {
        var name = text ?? string.Empty;
        if (!nativeState)
        {
            name += isChecked ? ", checked" : ", not checked";
        }

        return string.IsNullOrWhiteSpace(error) ? name : name + ". " + error;
    }

    public bool Toggle()
    {
        if (IsInputBlocked || !IsEnabled)
        {
            return false;
        }

        IsChecked = !IsChecked;
        return true;
    }

    private void OnTapped(object? sender, TappedEventArgs e) => Toggle();

    private void OnErrorTextChanged(string? errorText)
    {
        HasError = !string.IsNullOrEmpty(errorText);
        UpdateLook();
        if (errorText is { Length: > 0 } && Handler is not null)
        {
            SemanticScreenReader.Default.Announce(errorText);
        }
    }

    private void OnFocusRequested(int previous, int requested)
    {
        if (requested != previous && requested != 0 && Handler is not null)
        {
            Dispatcher.Dispatch(() => FocusMoves.MoveTo(this));
        }
    }

    private void UpdateLook()
    {
        SemanticProperties.SetDescription(this, Describe(Text, IsChecked, ErrorText, ExposesNativeState));
        if (Surface is null)
        {
            return;
        }

        if (HasError)
        {
            Surface.SetDynamicResource(Border.StrokeProperty, "FsDangerBrush");
            Surface.StrokeThickness = 1.5;
        }
        else
        {
            Surface.SetDynamicResource(Border.StrokeProperty, "FsGlassStrokeBrush");
            Surface.StrokeThickness = 1;
        }
    }
}
