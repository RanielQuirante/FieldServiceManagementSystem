using System.Windows.Input;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Screens;
using Microsoft.Maui.Controls.Shapes;

namespace FieldService.Mobile.Shared.Controls;

public enum AppHeaderVariant
{
    SignIn,
    List,
    Pushed,
    DesktopTopBar,
}

public partial class AppHeader : ContentView, ISystemBarInsetsAware
{
    public const double DesktopRadius = 0;

    public static readonly BindableProperty VariantProperty = BindableProperty.Create(
        nameof(Variant),
        typeof(AppHeaderVariant),
        typeof(AppHeader),
        AppHeaderVariant.SignIn,
        propertyChanged: (bindable, _, _) => ((AppHeader)bindable).ApplyVariant());

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(AppHeader));

    public static readonly BindableProperty SubtitleProperty = BindableProperty.Create(nameof(Subtitle), typeof(string), typeof(AppHeader));

    public static readonly BindableProperty EyebrowProperty = BindableProperty.Create(nameof(Eyebrow), typeof(string), typeof(AppHeader));

    public static readonly BindableProperty AvatarTextProperty = BindableProperty.Create(nameof(AvatarText), typeof(string), typeof(AppHeader));

    public static readonly BindableProperty AvatarDescriptionProperty = BindableProperty.Create(nameof(AvatarDescription), typeof(string), typeof(AppHeader), "Account and sign out");

    public static readonly BindableProperty AvatarCommandProperty = BindableProperty.Create(nameof(AvatarCommand), typeof(ICommand), typeof(AppHeader));

    public static readonly BindableProperty SyncTextProperty = BindableProperty.Create(
        nameof(SyncText),
        typeof(string),
        typeof(AppHeader),
        propertyChanged: (bindable, _, _) => ((AppHeader)bindable).ApplyText());

    public static readonly BindableProperty SyncIndicatorProperty = BindableProperty.Create(
        nameof(SyncIndicator),
        typeof(SyncIndicator),
        typeof(AppHeader),
        SyncIndicator.Updated,
        propertyChanged: (bindable, _, _) => ((AppHeader)bindable).ApplyIcons());

    public static readonly BindableProperty RefreshCommandProperty = BindableProperty.Create(nameof(RefreshCommand), typeof(ICommand), typeof(AppHeader));

    public static readonly BindableProperty RefreshDescriptionProperty = BindableProperty.Create(nameof(RefreshDescription), typeof(string), typeof(AppHeader), "Refresh jobs");

    public static readonly BindableProperty AccessoryProperty = BindableProperty.Create(
        nameof(Accessory),
        typeof(View),
        typeof(AppHeader),
        propertyChanged: (bindable, _, _) => ((AppHeader)bindable).ApplyAccessory());

    public static readonly BindableProperty PushedBodyProperty = BindableProperty.Create(
        nameof(PushedBody),
        typeof(View),
        typeof(AppHeader),
        propertyChanged: (bindable, _, _) => ((AppHeader)bindable).ApplyAccessory());

    public static readonly BindableProperty HasPushedBodyProperty = BindableProperty.Create(nameof(HasPushedBody), typeof(bool), typeof(AppHeader), false);

    public static readonly BindableProperty BackCommandProperty = BindableProperty.Create(nameof(BackCommand), typeof(ICommand), typeof(AppHeader));

    public static readonly BindableProperty BackDescriptionProperty = BindableProperty.Create(nameof(BackDescription), typeof(string), typeof(AppHeader), "Back to my jobs");

    public static readonly BindableProperty HasSummaryProperty = BindableProperty.Create(nameof(HasSummary), typeof(bool), typeof(AppHeader), false);

    public static readonly BindableProperty SummaryStatusProperty = BindableProperty.Create(
        nameof(SummaryStatus),
        typeof(JobStatus),
        typeof(AppHeader),
        JobStatus.Unknown,
        propertyChanged: (bindable, _, _) => ((AppHeader)bindable).ApplyIcons());

    public static readonly BindableProperty SummaryPriorityProperty = BindableProperty.Create(nameof(SummaryPriority), typeof(JobPriority), typeof(AppHeader), JobPriority.Unknown);

    public static readonly BindableProperty SummaryTitleProperty = BindableProperty.Create(nameof(SummaryTitle), typeof(string), typeof(AppHeader));

    public static readonly BindableProperty HasScheduleProperty = BindableProperty.Create(nameof(HasSchedule), typeof(bool), typeof(AppHeader), false);

    public static readonly BindableProperty ScheduleTextProperty = BindableProperty.Create(nameof(ScheduleText), typeof(string), typeof(AppHeader));

    public static readonly BindableProperty StartsTextProperty = BindableProperty.Create(
        nameof(StartsText),
        typeof(string),
        typeof(AppHeader),
        propertyChanged: (bindable, _, _) => ((AppHeader)bindable).ApplyText());

    public static readonly BindableProperty AssignedTextProperty = BindableProperty.Create(
        nameof(AssignedText),
        typeof(string),
        typeof(AppHeader),
        propertyChanged: (bindable, _, _) => ((AppHeader)bindable).ApplyText());

    public static readonly BindableProperty StatusLineTextProperty = BindableProperty.Create(
        nameof(StatusLineText),
        typeof(string),
        typeof(AppHeader),
        propertyChanged: (bindable, _, _) => ((AppHeader)bindable).ApplyText());

    public static readonly BindableProperty ReasonTextProperty = BindableProperty.Create(
        nameof(ReasonText),
        typeof(string),
        typeof(AppHeader),
        propertyChanged: (bindable, _, _) => ((AppHeader)bindable).ApplyText());

    public static readonly BindableProperty HasReasonTextProperty = BindableProperty.Create(nameof(HasReasonText), typeof(bool), typeof(AppHeader), false);

    public static readonly BindableProperty SummaryFocusRequestProperty = BindableProperty.Create(
        nameof(SummaryFocusRequest),
        typeof(int),
        typeof(AppHeader),
        0,
        propertyChanged: (bindable, oldValue, newValue) => ((AppHeader)bindable).OnSummaryFocusRequested((int)oldValue, (int)newValue));

    public static readonly BindableProperty UpdatedTextProperty = BindableProperty.Create(
        nameof(UpdatedText),
        typeof(string),
        typeof(AppHeader),
        propertyChanged: (bindable, _, _) => ((AppHeader)bindable).ApplyText());

    public static readonly BindableProperty StatusBarInsetProperty = BindableProperty.Create(
        nameof(StatusBarInset),
        typeof(double),
        typeof(AppHeader),
        0d,
        propertyChanged: (bindable, _, _) => ((AppHeader)bindable).ApplyVariant());

    public static readonly BindableProperty InsetPaddingProperty = BindableProperty.Create(nameof(InsetPadding), typeof(Thickness), typeof(AppHeader), Thickness.Zero);

    public static readonly BindableProperty IsSignInProperty = BindableProperty.Create(nameof(IsSignIn), typeof(bool), typeof(AppHeader), true);

    public static readonly BindableProperty IsListProperty = BindableProperty.Create(nameof(IsList), typeof(bool), typeof(AppHeader), false);

    public static readonly BindableProperty IsPushedProperty = BindableProperty.Create(nameof(IsPushed), typeof(bool), typeof(AppHeader), false);

    public static readonly BindableProperty IsDesktopTopBarProperty = BindableProperty.Create(nameof(IsDesktopTopBar), typeof(bool), typeof(AppHeader), false);

    public static readonly BindableProperty HasAccessoryProperty = BindableProperty.Create(nameof(HasAccessory), typeof(bool), typeof(AppHeader), false);

    public static readonly BindableProperty HasSyncTextProperty = BindableProperty.Create(nameof(HasSyncText), typeof(bool), typeof(AppHeader), false);

    public static readonly BindableProperty HasStartsTextProperty = BindableProperty.Create(nameof(HasStartsText), typeof(bool), typeof(AppHeader), false);

    public static readonly BindableProperty HasAssignedTextProperty = BindableProperty.Create(nameof(HasAssignedText), typeof(bool), typeof(AppHeader), false);

    public static readonly BindableProperty HasStatusLineProperty = BindableProperty.Create(nameof(HasStatusLine), typeof(bool), typeof(AppHeader), false);

    public static readonly BindableProperty HasUpdatedTextProperty = BindableProperty.Create(nameof(HasUpdatedText), typeof(bool), typeof(AppHeader), false);

    public AppHeader()
    {
        InitializeComponent();
        ApplyVariant();
        ApplyIcons();
        ApplyText();
    }

    public AppHeaderVariant Variant
    {
        get => (AppHeaderVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
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

    public string? Eyebrow
    {
        get => (string?)GetValue(EyebrowProperty);
        set => SetValue(EyebrowProperty, value);
    }

    public string? AvatarText
    {
        get => (string?)GetValue(AvatarTextProperty);
        set => SetValue(AvatarTextProperty, value);
    }

    public string? AvatarDescription
    {
        get => (string?)GetValue(AvatarDescriptionProperty);
        set => SetValue(AvatarDescriptionProperty, value);
    }

    public ICommand? AvatarCommand
    {
        get => (ICommand?)GetValue(AvatarCommandProperty);
        set => SetValue(AvatarCommandProperty, value);
    }

    public string? SyncText
    {
        get => (string?)GetValue(SyncTextProperty);
        set => SetValue(SyncTextProperty, value);
    }

    public SyncIndicator SyncIndicator
    {
        get => (SyncIndicator)GetValue(SyncIndicatorProperty);
        set => SetValue(SyncIndicatorProperty, value);
    }

    public ICommand? RefreshCommand
    {
        get => (ICommand?)GetValue(RefreshCommandProperty);
        set => SetValue(RefreshCommandProperty, value);
    }

    public string? RefreshDescription
    {
        get => (string?)GetValue(RefreshDescriptionProperty);
        set => SetValue(RefreshDescriptionProperty, value);
    }

    public View? Accessory
    {
        get => (View?)GetValue(AccessoryProperty);
        set => SetValue(AccessoryProperty, value);
    }

    public View? PushedBody
    {
        get => (View?)GetValue(PushedBodyProperty);
        set => SetValue(PushedBodyProperty, value);
    }

    public bool HasPushedBody
    {
        get => (bool)GetValue(HasPushedBodyProperty);
        private set => SetValue(HasPushedBodyProperty, value);
    }

    public ICommand? BackCommand
    {
        get => (ICommand?)GetValue(BackCommandProperty);
        set => SetValue(BackCommandProperty, value);
    }

    public string? BackDescription
    {
        get => (string?)GetValue(BackDescriptionProperty);
        set => SetValue(BackDescriptionProperty, value);
    }

    public bool HasSummary
    {
        get => (bool)GetValue(HasSummaryProperty);
        set => SetValue(HasSummaryProperty, value);
    }

    public JobStatus SummaryStatus
    {
        get => (JobStatus)GetValue(SummaryStatusProperty);
        set => SetValue(SummaryStatusProperty, value);
    }

    public JobPriority SummaryPriority
    {
        get => (JobPriority)GetValue(SummaryPriorityProperty);
        set => SetValue(SummaryPriorityProperty, value);
    }

    public string? SummaryTitle
    {
        get => (string?)GetValue(SummaryTitleProperty);
        set => SetValue(SummaryTitleProperty, value);
    }

    public bool HasSchedule
    {
        get => (bool)GetValue(HasScheduleProperty);
        set => SetValue(HasScheduleProperty, value);
    }

    public string? ScheduleText
    {
        get => (string?)GetValue(ScheduleTextProperty);
        set => SetValue(ScheduleTextProperty, value);
    }

    public string? StartsText
    {
        get => (string?)GetValue(StartsTextProperty);
        set => SetValue(StartsTextProperty, value);
    }

    public string? AssignedText
    {
        get => (string?)GetValue(AssignedTextProperty);
        set => SetValue(AssignedTextProperty, value);
    }

    public string? StatusLineText
    {
        get => (string?)GetValue(StatusLineTextProperty);
        set => SetValue(StatusLineTextProperty, value);
    }

    public string? UpdatedText
    {
        get => (string?)GetValue(UpdatedTextProperty);
        set => SetValue(UpdatedTextProperty, value);
    }

    public double StatusBarInset
    {
        get => (double)GetValue(StatusBarInsetProperty);
        set => SetValue(StatusBarInsetProperty, value);
    }

    public Thickness InsetPadding
    {
        get => (Thickness)GetValue(InsetPaddingProperty);
        private set => SetValue(InsetPaddingProperty, value);
    }

    public bool IsSignIn
    {
        get => (bool)GetValue(IsSignInProperty);
        private set => SetValue(IsSignInProperty, value);
    }

    public bool IsList
    {
        get => (bool)GetValue(IsListProperty);
        private set => SetValue(IsListProperty, value);
    }

    public bool IsPushed
    {
        get => (bool)GetValue(IsPushedProperty);
        private set => SetValue(IsPushedProperty, value);
    }

    public bool IsDesktopTopBar
    {
        get => (bool)GetValue(IsDesktopTopBarProperty);
        private set => SetValue(IsDesktopTopBarProperty, value);
    }

    public bool HasAccessory
    {
        get => (bool)GetValue(HasAccessoryProperty);
        private set => SetValue(HasAccessoryProperty, value);
    }

    public bool HasSyncText
    {
        get => (bool)GetValue(HasSyncTextProperty);
        private set => SetValue(HasSyncTextProperty, value);
    }

    public bool HasStartsText
    {
        get => (bool)GetValue(HasStartsTextProperty);
        private set => SetValue(HasStartsTextProperty, value);
    }

    public bool HasAssignedText
    {
        get => (bool)GetValue(HasAssignedTextProperty);
        private set => SetValue(HasAssignedTextProperty, value);
    }

    public string? ReasonText
    {
        get => (string?)GetValue(ReasonTextProperty);
        set => SetValue(ReasonTextProperty, value);
    }

    public bool HasReasonText
    {
        get => (bool)GetValue(HasReasonTextProperty);
        private set => SetValue(HasReasonTextProperty, value);
    }

    public int SummaryFocusRequest
    {
        get => (int)GetValue(SummaryFocusRequestProperty);
        set => SetValue(SummaryFocusRequestProperty, value);
    }

    public bool HasStatusLine
    {
        get => (bool)GetValue(HasStatusLineProperty);
        private set => SetValue(HasStatusLineProperty, value);
    }

    public bool HasUpdatedText
    {
        get => (bool)GetValue(HasUpdatedTextProperty);
        private set => SetValue(HasUpdatedTextProperty, value);
    }

    public static string SyncIconKey(SyncIndicator indicator) => indicator switch
    {
        SyncIndicator.Loading => "FsIconRefresh",
        SyncIndicator.Offline => "FsIconWifiOff",
        _ => "FsIconClock",
    };

    public void ApplySystemBarInsets(double statusBar, double navigationBar) => StatusBarInset = Math.Max(0, statusBar);

    public static double BottomRadius(AppHeaderVariant variant) => variant switch
    {
        AppHeaderVariant.SignIn => 32,
        AppHeaderVariant.DesktopTopBar => DesktopRadius,
        _ => 28,
    };

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        ApplyAccessory();
    }

    private void ApplyVariant()
    {
        IsSignIn = Variant == AppHeaderVariant.SignIn;
        IsList = Variant == AppHeaderVariant.List;
        IsPushed = Variant == AppHeaderVariant.Pushed;
        IsDesktopTopBar = Variant == AppHeaderVariant.DesktopTopBar;
        InsetPadding = IsDesktopTopBar ? Thickness.Zero : new Thickness(0, StatusBarInset, 0, 0);
        if (Hero is null)
        {
            return;
        }

        var radius = BottomRadius(Variant);
        var shape = new RoundRectangle { CornerRadius = new CornerRadius(0, 0, radius, radius) };
        Hero.StrokeShape = shape;
        Glow.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(0, 0, radius, radius) };
        Hero.SetDynamicResource(Border.ShadowProperty, IsDesktopTopBar ? "FsShadowTopBar" : "FsShadowHero");
    }

    private void ApplyIcons()
    {
        if (SyncIcon is null)
        {
            return;
        }

        SyncIcon.SetDynamicResource(Icon.PathDataProperty, SyncIconKey(SyncIndicator));
        StatusLineIcon.SetDynamicResource(Icon.PathDataProperty, StatusChip.ResourceKeys(SummaryStatus).Icon);
    }

    private void ApplyText()
    {
        HasSyncText = !string.IsNullOrWhiteSpace(SyncText);
        HasStartsText = !string.IsNullOrWhiteSpace(StartsText);
        HasAssignedText = !string.IsNullOrWhiteSpace(AssignedText);
        HasStatusLine = !string.IsNullOrWhiteSpace(StatusLineText);
        HasReasonText = !string.IsNullOrWhiteSpace(ReasonText);
        HasUpdatedText = !string.IsNullOrWhiteSpace(UpdatedText);
    }

    private void OnSummaryFocusRequested(int previous, int requested)
    {
        if (requested != previous && requested != 0 && SummaryTitleLabel?.Handler is not null)
        {
            Dispatcher.Dispatch(() => SummaryTitleLabel.SetSemanticFocus());
        }
    }

    private void ApplyAccessory()
    {
        HasAccessory = Accessory is not null;
        HasPushedBody = PushedBody is not null;
        if (ListAccessory is null)
        {
            return;
        }

        if (Accessory is { } accessory)
        {
            accessory.BindingContext = BindingContext;
        }

        if (PushedBody is { } body)
        {
            body.BindingContext = BindingContext;
        }

        ListAccessory.Content = Accessory;
        PushedAccessory.Content = PushedBody;
    }
}
