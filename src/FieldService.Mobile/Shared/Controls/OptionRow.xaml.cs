using System.Windows.Input;

namespace FieldService.Mobile.Shared.Controls;

public partial class OptionRow : ContentView
{
    public const string AndroidClassName = "android.widget.RadioButton";
    public const string DisabledIconKey = "FsIconLock";

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title),
        typeof(string),
        typeof(OptionRow),
        propertyChanged: (bindable, _, _) => ((OptionRow)bindable).UpdateLook());

    public static readonly BindableProperty HelperProperty = BindableProperty.Create(
        nameof(Helper),
        typeof(string),
        typeof(OptionRow),
        propertyChanged: (bindable, _, _) => ((OptionRow)bindable).UpdateLook());

    public static readonly BindableProperty IconKeyProperty = BindableProperty.Create(
        nameof(IconKey),
        typeof(string),
        typeof(OptionRow),
        propertyChanged: (bindable, _, _) => ((OptionRow)bindable).UpdateLook());

    public static readonly BindableProperty IconColorKeyProperty = BindableProperty.Create(
        nameof(IconColorKey),
        typeof(string),
        typeof(OptionRow),
        propertyChanged: (bindable, _, _) => ((OptionRow)bindable).UpdateLook());

    public static readonly BindableProperty IsSelectedProperty = BindableProperty.Create(
        nameof(IsSelected),
        typeof(bool),
        typeof(OptionRow),
        false,
        propertyChanged: (bindable, _, _) => ((OptionRow)bindable).UpdateLook());

    public static readonly BindableProperty IsAvailableProperty = BindableProperty.Create(
        nameof(IsAvailable),
        typeof(bool),
        typeof(OptionRow),
        true,
        propertyChanged: (bindable, _, _) => ((OptionRow)bindable).UpdateLook());

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(OptionRow));

    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(OptionRow));

    public static readonly BindableProperty HasHelperProperty = BindableProperty.Create(nameof(HasHelper), typeof(bool), typeof(OptionRow), false);

    public static readonly BindableProperty ShowsDotProperty = BindableProperty.Create(nameof(ShowsDot), typeof(bool), typeof(OptionRow), false);

    public static readonly BindableProperty DescriptionProperty = BindableProperty.Create(nameof(Description), typeof(string), typeof(OptionRow), string.Empty);

    public OptionRow()
    {
        InitializeComponent();
        UpdateLook();
    }

    public static bool ExposesNativeState => OperatingSystem.IsAndroid();

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Helper
    {
        get => (string?)GetValue(HelperProperty);
        set => SetValue(HelperProperty, value);
    }

    public string? IconKey
    {
        get => (string?)GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    public string? IconColorKey
    {
        get => (string?)GetValue(IconColorKeyProperty);
        set => SetValue(IconColorKeyProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public bool IsAvailable
    {
        get => (bool)GetValue(IsAvailableProperty);
        set => SetValue(IsAvailableProperty, value);
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

    public bool HasHelper
    {
        get => (bool)GetValue(HasHelperProperty);
        private set => SetValue(HasHelperProperty, value);
    }

    public bool ShowsDot
    {
        get => (bool)GetValue(ShowsDotProperty);
        private set => SetValue(ShowsDotProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        private set => SetValue(DescriptionProperty, value);
    }

    public static string Describe(string? title, string? helper, bool isSelected, bool isAvailable, bool nativeState)
    {
        var name = title ?? string.Empty;
        if (!nativeState)
        {
            name += !isAvailable ? ", unavailable" : isSelected ? ", selected" : ", not selected";
        }

        return string.IsNullOrWhiteSpace(helper) ? name : name + ". " + helper;
    }

    public static OptionRowLook LookFor(bool isSelected, bool isAvailable) => (isSelected, isAvailable) switch
    {
        (_, false) => new OptionRowLook("FsOptionDisabledFillBrush", "FsHairlineBrush", 1, "FsHairlineBrush", "FsMuted"),
        (true, true) => new OptionRowLook("FsOptionSelectedFillBrush", "FsTeal700Brush", 1.5, "FsTeal700Brush", "FsInk"),
        _ => new OptionRowLook("FsTransparentBrush", "FsOptionStrokeBrush", 1, "FsLineInputBrush", "FsInk"),
    };

    public static Thickness PaddingFor(double strokeThickness) =>
        new(16 - strokeThickness, 14 - strokeThickness, 16 - strokeThickness, 14);

    public bool Activate()
    {
        if (!IsAvailable || Command is not { } command || !command.CanExecute(CommandParameter))
        {
            return false;
        }

        command.Execute(CommandParameter);
        return true;
    }

    private void OnTapped(object? sender, TappedEventArgs e) => Activate();

    private void UpdateLook()
    {
        HasHelper = !string.IsNullOrWhiteSpace(Helper);
        ShowsDot = IsSelected && IsAvailable;
        Description = Describe(Title, Helper, IsSelected, IsAvailable, ExposesNativeState);
        SemanticProperties.SetDescription(this, Description);
        if (Surface is null)
        {
            return;
        }

        var look = LookFor(IsSelected, IsAvailable);
        Surface.SetDynamicResource(Border.BackgroundProperty, look.Surface);
        Surface.SetDynamicResource(Border.StrokeProperty, look.Stroke);
        Surface.StrokeThickness = look.StrokeThickness;
        Surface.Padding = PaddingFor(look.StrokeThickness);
        Ring.SetDynamicResource(Border.StrokeProperty, look.Ring);
        TitleLabel.SetDynamicResource(Label.TextColorProperty, look.Title);
        TrailingIcon.Size = IsAvailable ? 20 : 18;
        TrailingIcon.SetDynamicResource(Icon.PathDataProperty, IsAvailable ? IconKey ?? DisabledIconKey : DisabledIconKey);
        TrailingIcon.SetDynamicResource(Icon.ColorProperty, IsAvailable ? IconColorKey ?? "FsMuted" : "FsDisabledText");
    }
}

public sealed record OptionRowLook(string Surface, string Stroke, double StrokeThickness, string Ring, string Title);
