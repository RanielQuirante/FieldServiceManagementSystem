using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FieldService.Mobile.Shared.Controls;

public enum BannerVariant
{
    Info,
    Offline,
    Error,
    Conflict,
    Locked,
    Success,
}

public partial class Banner : ContentView
{
    public static readonly BindableProperty VariantProperty = BindableProperty.Create(
        nameof(Variant),
        typeof(BannerVariant),
        typeof(Banner),
        BannerVariant.Info,
        propertyChanged: (bindable, _, _) => ((Banner)bindable).ApplyVariant());

    public static readonly BindableProperty IconKeyProperty = BindableProperty.Create(
        nameof(IconKey),
        typeof(string),
        typeof(Banner),
        propertyChanged: (bindable, _, _) => ((Banner)bindable).ApplyVariant());

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title),
        typeof(string),
        typeof(Banner),
        propertyChanged: (bindable, _, _) => ((Banner)bindable).OnTextChanged());

    public static readonly BindableProperty MessageProperty = BindableProperty.Create(
        nameof(Message),
        typeof(string),
        typeof(Banner),
        propertyChanged: (bindable, _, _) => ((Banner)bindable).OnTextChanged());

    public static readonly BindableProperty ReferenceProperty = BindableProperty.Create(
        nameof(Reference),
        typeof(string),
        typeof(Banner),
        propertyChanged: (bindable, _, _) => ((Banner)bindable).OnTextChanged());

    public static readonly BindableProperty ActionTextProperty = BindableProperty.Create(
        nameof(ActionText),
        typeof(string),
        typeof(Banner),
        propertyChanged: (bindable, _, _) => ((Banner)bindable).OnTextChanged());

    public static readonly BindableProperty ActionCommandProperty = BindableProperty.Create(nameof(ActionCommand), typeof(ICommand), typeof(Banner));

    public static readonly BindableProperty HasReferenceProperty = BindableProperty.Create(nameof(HasReference), typeof(bool), typeof(Banner), false);

    public static readonly BindableProperty HasActionProperty = BindableProperty.Create(nameof(HasAction), typeof(bool), typeof(Banner), false);

    public static readonly BindableProperty MessageSeparatorProperty = BindableProperty.Create(nameof(MessageSeparator), typeof(string), typeof(Banner), string.Empty);

    public static readonly BindableProperty SpokenTextProperty = BindableProperty.Create(nameof(SpokenText), typeof(string), typeof(Banner), string.Empty);

    public static readonly BindableProperty AnnouncesProperty = BindableProperty.Create(nameof(Announces), typeof(bool), typeof(Banner), true);

    public static readonly BindableProperty LeaveFocusCommandProperty = BindableProperty.Create(nameof(LeaveFocusCommand), typeof(ICommand), typeof(Banner));

    private BannerText? _announced;
    private bool _announcePending;
    private bool _focusWithinOnLeave;
    private bool _focusSeenThisTurn;

    public Banner()
    {
        InitializeComponent();
        ApplyVariant();
        OnTextChanged();
    }

    public BannerVariant Variant
    {
        get => (BannerVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public string? IconKey
    {
        get => (string?)GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
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

    public string? Reference
    {
        get => (string?)GetValue(ReferenceProperty);
        set => SetValue(ReferenceProperty, value);
    }

    public string? ActionText
    {
        get => (string?)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    public bool HasReference
    {
        get => (bool)GetValue(HasReferenceProperty);
        private set => SetValue(HasReferenceProperty, value);
    }

    public bool HasAction
    {
        get => (bool)GetValue(HasActionProperty);
        private set => SetValue(HasActionProperty, value);
    }

    public string MessageSeparator
    {
        get => (string)GetValue(MessageSeparatorProperty);
        private set => SetValue(MessageSeparatorProperty, value);
    }

    public string SpokenText
    {
        get => (string)GetValue(SpokenTextProperty);
        private set => SetValue(SpokenTextProperty, value);
    }

    public bool Announces
    {
        get => (bool)GetValue(AnnouncesProperty);
        set => SetValue(AnnouncesProperty, value);
    }

    public ICommand? LeaveFocusCommand
    {
        get => (ICommand?)GetValue(LeaveFocusCommandProperty);
        set => SetValue(LeaveFocusCommandProperty, value);
    }

    public static bool ShouldAnnounce(BannerText? announced, BannerText next)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (!next.HasText)
        {
            return false;
        }

        return announced is null
            || Adds(announced.Title, next.Title)
            || Adds(announced.Message, next.Message)
            || Adds(announced.Reference, next.Reference);
    }

    private static bool Adds(string? before, string? after) =>
        !string.IsNullOrWhiteSpace(after) && !string.Equals(before, after, StringComparison.Ordinal);

    public static string DefaultIconKey(BannerVariant variant) => variant switch
    {
        BannerVariant.Error => "FsIconAlertTriangle",
        BannerVariant.Offline => "FsIconWifiOff",
        BannerVariant.Conflict => "FsIconRefresh",
        BannerVariant.Locked => "FsIconLock",
        BannerVariant.Success => "FsIconCheckCircle",
        _ => "FsIconClock",
    };

    public static BannerLook LookFor(BannerVariant variant) => variant switch
    {
        BannerVariant.Error => new BannerLook("FsBannerErrorBrush", "FsDangerBannerStrokeBrush", "FsPaddingBanner", "FsShadowDanger", "FsDangerBrush", "FsTransparentBrush", "FsOnDark"),
        BannerVariant.Conflict => new BannerLook("FsBannerWarningBrush", "FsWarningBannerStrokeBrush", "FsPaddingBanner", "FsShadowWarning", "FsAccentBrush", "FsTransparentBrush", "FsInk"),
        BannerVariant.Success => new BannerLook("FsBannerSuccessBrush", "FsSuccessBannerStrokeBrush", "FsPaddingBannerSurface", "FsShadowSuccess", "FsPassBrush", "FsTransparentBrush", "FsOnDark"),
        _ => new BannerLook("FsGlassBrush", "FsGlassStrokeBrush", "FsPaddingBannerSurface", "FsShadowGlass", "FsBannerPausedTileBrush", "FsRingMobileBrush", "FsInk2"),
    };

    public static string Speak(string? title, string? message, string? reference)
    {
        var parts = new[] { title, message, string.IsNullOrEmpty(reference) ? null : "Reference " + reference }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        return string.Join(" ", parts);
    }

    protected override void OnPropertyChanging([CallerMemberName] string? propertyName = null)
    {
        base.OnPropertyChanging(propertyName);
        if (propertyName == IsVisibleProperty.PropertyName)
        {
            _focusWithinOnLeave = IsVisible && LeaveFocusCommand is not null && (_focusSeenThisTurn || FocusMoves.HasFocusWithin(this) == true);
        }
    }

    protected override void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName != IsVisibleProperty.PropertyName)
        {
            return;
        }

        if (!IsVisible)
        {
            _announced = null;
            if (_focusWithinOnLeave && LeaveFocusCommand is { } command && command.CanExecute(null))
            {
                command.Execute(null);
            }
        }

        _focusWithinOnLeave = false;
        ScheduleAnnounce();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        ScheduleAnnounce();
    }

    private void ApplyVariant()
    {
        if (Surface is null)
        {
            return;
        }

        var look = LookFor(Variant);
        Surface.SetDynamicResource(Border.BackgroundProperty, look.Surface);
        Surface.SetDynamicResource(Border.StrokeProperty, look.Stroke);
        Surface.SetDynamicResource(Border.PaddingProperty, look.Padding);
        Surface.SetDynamicResource(Border.ShadowProperty, look.Shadow);
        Tile.SetDynamicResource(Border.BackgroundProperty, look.Tile);
        Tile.SetDynamicResource(Border.StrokeProperty, look.TileStroke);
        TileIcon.SetDynamicResource(Icon.ColorProperty, look.TileIcon);
        TileIcon.SetDynamicResource(Icon.PathDataProperty, IconKey ?? DefaultIconKey(Variant));
    }

    private void OnTextChanged()
    {
        HasReference = !string.IsNullOrEmpty(Reference);
        HasAction = !string.IsNullOrEmpty(ActionText);
        MessageSeparator = string.IsNullOrEmpty(Message) ? string.Empty : " ";
        SpokenText = Speak(Title, Message, Reference);
        NoteFocusWithin();
        ScheduleAnnounce();
    }

    private void NoteFocusWithin()
    {
        if (_focusSeenThisTurn || !IsVisible || LeaveFocusCommand is null || Handler is null || FocusMoves.HasFocusWithin(this) != true)
        {
            return;
        }

        _focusSeenThisTurn = true;
        Dispatcher.Dispatch(() => _focusSeenThisTurn = false);
    }

    private void ScheduleAnnounce()
    {
        if (_announcePending || Handler is null)
        {
            return;
        }

        _announcePending = true;
        Dispatcher.Dispatch(() =>
        {
            _announcePending = false;
            Announce();
        });
    }

    private void Announce()
    {
        var text = new BannerText(Title, Message, Reference);
        if (!Announces || !IsVisible || Handler is null || !ShouldAnnounce(_announced, text))
        {
            return;
        }

        _announced = text;
        SemanticScreenReader.Default.Announce(SpokenText);
    }
}

public sealed record BannerLook(string Surface, string Stroke, string Padding, string Shadow, string Tile, string TileStroke, string TileIcon);

public sealed record BannerText(string? Title, string? Message, string? Reference)
{
    public bool HasText => !string.IsNullOrWhiteSpace(Banner.Speak(Title, Message, Reference));
}
