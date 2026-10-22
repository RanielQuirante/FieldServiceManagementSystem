using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FieldService.Mobile.Shared.Controls;

public enum BannerVariant
{
    Info,
    Offline,
    Error,
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

    private string? _announced;

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

    public static string DefaultIconKey(BannerVariant variant) => variant switch
    {
        BannerVariant.Error => "FsIconAlertTriangle",
        BannerVariant.Offline => "FsIconWifiOff",
        _ => "FsIconClock",
    };

    public static string Speak(string? title, string? message, string? reference)
    {
        var parts = new[] { title, message, string.IsNullOrEmpty(reference) ? null : "Reference " + reference }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        return string.Join(" ", parts);
    }

    protected override void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == IsVisibleProperty.PropertyName)
        {
            if (!IsVisible)
            {
                _announced = null;
            }

            Announce();
        }
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        Announce();
    }

    private void ApplyVariant()
    {
        if (Surface is null)
        {
            return;
        }

        var error = Variant == BannerVariant.Error;
        Surface.SetDynamicResource(Border.BackgroundProperty, error ? "FsBannerErrorBrush" : "FsGlassBrush");
        Surface.SetDynamicResource(Border.StrokeProperty, error ? "FsDangerBannerStrokeBrush" : "FsGlassStrokeBrush");
        Surface.SetDynamicResource(Border.PaddingProperty, error ? "FsPaddingBanner" : "FsPaddingBannerSurface");
        Surface.SetDynamicResource(Border.ShadowProperty, error ? "FsShadowDanger" : "FsShadowGlass");
        Tile.SetDynamicResource(Border.BackgroundProperty, error ? "FsDangerBrush" : "FsBannerPausedTileBrush");
        Tile.SetDynamicResource(Border.StrokeProperty, error ? "FsTransparentBrush" : "FsRingMobileBrush");
        TileIcon.SetDynamicResource(Icon.ColorProperty, error ? "FsOnDark" : "FsInk2");
        TileIcon.SetDynamicResource(Icon.PathDataProperty, IconKey ?? DefaultIconKey(Variant));
    }

    private void OnTextChanged()
    {
        HasReference = !string.IsNullOrEmpty(Reference);
        HasAction = !string.IsNullOrEmpty(ActionText);
        MessageSeparator = string.IsNullOrEmpty(Message) ? string.Empty : " ";
        SpokenText = Speak(Title, Message, Reference);
        _announced = null;
        Announce();
    }

    private void Announce()
    {
        if (!IsVisible || Handler is null || string.IsNullOrWhiteSpace(SpokenText) || SpokenText == _announced)
        {
            return;
        }

        _announced = SpokenText;
        SemanticScreenReader.Default.Announce(SpokenText);
    }
}
