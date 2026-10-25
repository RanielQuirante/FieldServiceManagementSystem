using System.Diagnostics;
using System.Windows.Input;

namespace FieldService.Mobile.Shared.Controls;

public partial class SegmentedControl : ContentView
{
    public static readonly TimeSpan KeepFocusFor = TimeSpan.FromSeconds(10);

    public static readonly BindableProperty FirstTextProperty = BindableProperty.Create(nameof(FirstText), typeof(string), typeof(SegmentedControl));

    public static readonly BindableProperty FirstCountProperty = BindableProperty.Create(
        nameof(FirstCount),
        typeof(string),
        typeof(SegmentedControl),
        propertyChanged: (bindable, _, _) => ((SegmentedControl)bindable).Apply());

    public static readonly BindableProperty FirstDescriptionProperty = BindableProperty.Create(nameof(FirstDescription), typeof(string), typeof(SegmentedControl));

    public static readonly BindableProperty FirstParameterProperty = BindableProperty.Create(nameof(FirstParameter), typeof(object), typeof(SegmentedControl));

    public static readonly BindableProperty IsFirstSelectedProperty = BindableProperty.Create(
        nameof(IsFirstSelected),
        typeof(bool),
        typeof(SegmentedControl),
        true,
        propertyChanged: (bindable, _, _) => ((SegmentedControl)bindable).Apply());

    public static readonly BindableProperty SecondTextProperty = BindableProperty.Create(nameof(SecondText), typeof(string), typeof(SegmentedControl));

    public static readonly BindableProperty SecondCountProperty = BindableProperty.Create(
        nameof(SecondCount),
        typeof(string),
        typeof(SegmentedControl),
        propertyChanged: (bindable, _, _) => ((SegmentedControl)bindable).Apply());

    public static readonly BindableProperty SecondDescriptionProperty = BindableProperty.Create(nameof(SecondDescription), typeof(string), typeof(SegmentedControl));

    public static readonly BindableProperty SecondParameterProperty = BindableProperty.Create(nameof(SecondParameter), typeof(object), typeof(SegmentedControl));

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(SegmentedControl));

    public static readonly BindableProperty IsOnDarkProperty = BindableProperty.Create(
        nameof(IsOnDark),
        typeof(bool),
        typeof(SegmentedControl),
        false,
        propertyChanged: (bindable, _, _) => ((SegmentedControl)bindable).Apply());

    public static readonly BindableProperty IsCompactProperty = BindableProperty.Create(
        nameof(IsCompact),
        typeof(bool),
        typeof(SegmentedControl),
        false,
        propertyChanged: (bindable, _, _) => ((SegmentedControl)bindable).Apply());

    public static readonly BindableProperty HasFirstCountProperty = BindableProperty.Create(nameof(HasFirstCount), typeof(bool), typeof(SegmentedControl), false);

    public static readonly BindableProperty HasSecondCountProperty = BindableProperty.Create(nameof(HasSecondCount), typeof(bool), typeof(SegmentedControl), false);

    private Button? _chosen;
    private long _chosenAt;

    public SegmentedControl()
    {
        InitializeComponent();
        FirstButton.Clicked += OnSegmentClicked;
        SecondButton.Clicked += OnSegmentClicked;
        FirstButton.Loaded += OnSegmentLoaded;
        SecondButton.Loaded += OnSegmentLoaded;
        Apply();
    }

    public string? FirstText
    {
        get => (string?)GetValue(FirstTextProperty);
        set => SetValue(FirstTextProperty, value);
    }

    public string? FirstCount
    {
        get => (string?)GetValue(FirstCountProperty);
        set => SetValue(FirstCountProperty, value);
    }

    public string? FirstDescription
    {
        get => (string?)GetValue(FirstDescriptionProperty);
        set => SetValue(FirstDescriptionProperty, value);
    }

    public object? FirstParameter
    {
        get => GetValue(FirstParameterProperty);
        set => SetValue(FirstParameterProperty, value);
    }

    public bool IsFirstSelected
    {
        get => (bool)GetValue(IsFirstSelectedProperty);
        set => SetValue(IsFirstSelectedProperty, value);
    }

    public string? SecondText
    {
        get => (string?)GetValue(SecondTextProperty);
        set => SetValue(SecondTextProperty, value);
    }

    public string? SecondCount
    {
        get => (string?)GetValue(SecondCountProperty);
        set => SetValue(SecondCountProperty, value);
    }

    public string? SecondDescription
    {
        get => (string?)GetValue(SecondDescriptionProperty);
        set => SetValue(SecondDescriptionProperty, value);
    }

    public object? SecondParameter
    {
        get => GetValue(SecondParameterProperty);
        set => SetValue(SecondParameterProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public bool IsOnDark
    {
        get => (bool)GetValue(IsOnDarkProperty);
        set => SetValue(IsOnDarkProperty, value);
    }

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    public bool HasFirstCount
    {
        get => (bool)GetValue(HasFirstCountProperty);
        private set => SetValue(HasFirstCountProperty, value);
    }

    public bool HasSecondCount
    {
        get => (bool)GetValue(HasSecondCountProperty);
        private set => SetValue(HasSecondCountProperty, value);
    }

    public static CapsuleVariant CapsuleFor(bool selected, bool onDark) =>
        selected ? CapsuleVariant.Selected : onDark ? CapsuleVariant.OnDark : CapsuleVariant.Default;

    public static string FontSizeKey(bool compact) => compact ? "FsFontSizeSegmentCompact" : "FsFontSizeSegment";

    public static bool KeepsFocus(TimeSpan sinceChoice) => sinceChoice >= TimeSpan.Zero && sinceChoice <= KeepFocusFor;

    private Button SelectedButton => IsFirstSelected ? FirstButton : SecondButton;

    private void OnSegmentClicked(object? sender, EventArgs e)
    {
        _chosen = sender as Button;
        _chosenAt = Stopwatch.GetTimestamp();
        KeepFocusOnChoice();
    }

    private void OnSegmentLoaded(object? sender, EventArgs e)
    {
        if (_chosen is not null && ReferenceEquals(sender, _chosen) && KeepsFocus(Stopwatch.GetElapsedTime(_chosenAt)))
        {
            KeepFocusOnChoice();
        }
    }

    private void KeepFocusOnChoice()
    {
        if (_chosen is { } chosen)
        {
            FocusMoves.MoveSoon(chosen, () => ReferenceEquals(chosen, _chosen) && ReferenceEquals(chosen, SelectedButton));
        }
    }

    private void Apply()
    {
        HasFirstCount = !string.IsNullOrEmpty(FirstCount);
        HasSecondCount = !string.IsNullOrEmpty(SecondCount);
        if (Track is null)
        {
            return;
        }

        Track.SetDynamicResource(Border.BackgroundProperty, IsOnDark ? "FsGlassOnDarkBrush" : "FsTrackBrush");
        Track.SetDynamicResource(Border.StrokeProperty, IsOnDark ? "FsGlassDarkTrackStrokeBrush" : "FsTransparentBrush");
        Track.StrokeThickness = IsOnDark ? 1 : 0;
        Track.SetDynamicResource(Border.PaddingProperty, "FsPaddingSegmentTrack");
        ApplySegment(FirstSurface, FirstLabel, FirstLabelWidth, FirstCapsule, FirstButton, IsFirstSelected);
        ApplySegment(SecondSurface, SecondLabel, SecondLabelWidth, SecondCapsule, SecondButton, !IsFirstSelected);
    }

    private void ApplySegment(Border surface, Label label, Label width, CountCapsule capsule, Button button, bool selected)
    {
        surface.SetDynamicResource(Border.BackgroundProperty, selected ? "FsSegmentSelectedBrush" : "FsTransparentBrush");
        surface.SetDynamicResource(Border.ShadowProperty, selected ? IsOnDark ? "FsShadowSegmentOnDark" : "FsShadowSegment" : "FsShadowNone");

        label.FontFamily = selected ? "GeistSemiBold" : "GeistMedium";
        label.SetDynamicResource(Label.FontSizeProperty, FontSizeKey(IsCompact));
        width.SetDynamicResource(Label.FontSizeProperty, FontSizeKey(IsCompact));
        label.SetDynamicResource(Label.TextColorProperty, selected ? "FsInk" : IsOnDark ? "FsOnDark2" : "FsInk2");
        capsule.Variant = CapsuleFor(selected, IsOnDark);
        button.SetDynamicResource(StyleProperty, IsOnDark ? "FsOverlayButtonOnDark" : "FsOverlayButton");
    }
}
