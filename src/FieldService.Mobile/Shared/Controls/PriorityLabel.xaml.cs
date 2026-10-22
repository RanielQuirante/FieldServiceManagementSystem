using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Formatting;

namespace FieldService.Mobile.Shared.Controls;

public partial class PriorityLabel : ContentView
{
    public static readonly BindableProperty PriorityProperty = BindableProperty.Create(
        nameof(Priority),
        typeof(JobPriority),
        typeof(PriorityLabel),
        JobPriority.Unknown,
        propertyChanged: (bindable, _, _) => ((PriorityLabel)bindable).Apply());

    public static readonly BindableProperty IsOnDarkProperty = BindableProperty.Create(
        nameof(IsOnDark),
        typeof(bool),
        typeof(PriorityLabel),
        false,
        propertyChanged: (bindable, _, _) => ((PriorityLabel)bindable).Apply());

    public static readonly BindableProperty IsCompactProperty = BindableProperty.Create(
        nameof(IsCompact),
        typeof(bool),
        typeof(PriorityLabel),
        false,
        propertyChanged: (bindable, _, _) => ((PriorityLabel)bindable).Apply());

    public static readonly BindableProperty LabelProperty = BindableProperty.Create(nameof(Label), typeof(string), typeof(PriorityLabel), string.Empty);

    public static readonly BindableProperty IsBadgeProperty = BindableProperty.Create(nameof(IsBadge), typeof(bool), typeof(PriorityLabel), false);

    public static readonly BindableProperty IsGlyphProperty = BindableProperty.Create(nameof(IsGlyph), typeof(bool), typeof(PriorityLabel), false);

    public PriorityLabel()
    {
        InitializeComponent();
        Apply();
    }

    public JobPriority Priority
    {
        get => (JobPriority)GetValue(PriorityProperty);
        set => SetValue(PriorityProperty, value);
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

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        private set => SetValue(LabelProperty, value);
    }

    public bool IsBadge
    {
        get => (bool)GetValue(IsBadgeProperty);
        private set => SetValue(IsBadgeProperty, value);
    }

    public bool IsGlyph
    {
        get => (bool)GetValue(IsGlyphProperty);
        private set => SetValue(IsGlyphProperty, value);
    }

    public static string Description(JobPriority priority) => "Priority: " + DisplayFormats.PriorityLabel(priority);

    public static (string Icon, string Color, string Font) GlyphKeys(JobPriority priority, bool onDark) => priority switch
    {
        JobPriority.Low => ("FsIconChevronDown", onDark ? "FsOnDark" : "FsMuted", onDark ? "GeistSemiBold" : "GeistMedium"),
        JobPriority.High => ("FsIconChevronUp", onDark ? "FsOnDark" : "FsHigh", "GeistSemiBold"),
        _ => ("FsIconMinus", onDark ? "FsOnDark" : "FsInk2", onDark ? "GeistSemiBold" : "GeistMedium"),
    };

    private void Apply()
    {
        IsBadge = Priority == JobPriority.Urgent;
        IsGlyph = !IsBadge && Priority != JobPriority.Unknown;
        Label = DisplayFormats.PriorityLabel(Priority);
        IsVisible = Priority != JobPriority.Unknown;
        SemanticProperties.SetDescription(this, Description(Priority));
        if (Pill is null)
        {
            return;
        }

        var (icon, color, font) = GlyphKeys(Priority, IsOnDark);
        GlyphIcon.SetDynamicResource(Icon.PathDataProperty, icon);
        GlyphIcon.SetDynamicResource(Icon.ColorProperty, color);
        GlyphLabel.SetDynamicResource(Microsoft.Maui.Controls.Label.TextColorProperty, color);
        GlyphLabel.FontFamily = font;
        GlyphLabel.SetDynamicResource(
            Microsoft.Maui.Controls.Label.FontSizeProperty,
            IsOnDark || IsCompact ? "FsFontSizePriorityCompact" : "FsFontSizePriority");
        PillContent.Spacing = IsOnDark ? 6 : 5;
        Pill.SetDynamicResource(Border.BackgroundProperty, IsOnDark ? "FsGlassOnDarkControlBrush" : "FsTransparentBrush");
        Pill.SetDynamicResource(Border.StrokeProperty, IsOnDark ? "FsGlassDarkPillStrokeBrush" : "FsTransparentBrush");
        Pill.StrokeThickness = IsOnDark ? 1 : 0;
        Pill.SetDynamicResource(Border.PaddingProperty, IsOnDark ? "FsPaddingPill" : "FsPaddingNone");
        Pill.SetDynamicResource(MinimumHeightRequestProperty, IsOnDark ? "FsHeightChip" : "FsSizeZero");

        Badge.SetDynamicResource(MinimumHeightRequestProperty, IsCompact ? "FsHeightUrgentBadgeCompact" : "FsHeightUrgentBadge");
        BadgeLabel.SetDynamicResource(
            Microsoft.Maui.Controls.Label.FontSizeProperty,
            IsCompact ? "FsFontSizeUrgentCompact" : "FsFontSizeUrgent");
        BadgeIcon.Size = IsCompact ? 14 : 16;
    }
}
