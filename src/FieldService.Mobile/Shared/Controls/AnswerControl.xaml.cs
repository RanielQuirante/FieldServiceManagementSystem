using System.Windows.Input;
using FieldService.Mobile.Features.Inspection;

namespace FieldService.Mobile.Shared.Controls;

public partial class AnswerControl : ContentView
{
    public const double OptionSpacing = 8;
    public const double OptionPadding = 6;
    public const double OptionStroke = 1.5;
    public const double IconSize = 18;
    public const double IconGap = 6;

    public static readonly BindableProperty ResultProperty = BindableProperty.Create(
        nameof(Result),
        typeof(InspectionResult?),
        typeof(AnswerControl),
        null,
        propertyChanged: (bindable, _, _) => ((AnswerControl)bindable).ApplyLook());

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(AnswerControl));

    public static readonly BindableProperty GroupDescriptionProperty = BindableProperty.Create(nameof(GroupDescription), typeof(string), typeof(AnswerControl));

    public static readonly BindableProperty PassDescriptionProperty = BindableProperty.Create(nameof(PassDescription), typeof(string), typeof(AnswerControl), string.Empty);

    public static readonly BindableProperty FailDescriptionProperty = BindableProperty.Create(nameof(FailDescription), typeof(string), typeof(AnswerControl), string.Empty);

    public static readonly BindableProperty NotApplicableDescriptionProperty = BindableProperty.Create(nameof(NotApplicableDescription), typeof(string), typeof(AnswerControl), string.Empty);

    public static readonly BindableProperty IsStackedProperty = BindableProperty.Create(nameof(IsStacked), typeof(bool), typeof(AnswerControl), false);

    private double _measuredFor = -1;

    public AnswerControl()
    {
        InitializeComponent();
        ApplyLook();
        SizeChanged += (_, _) => FitToWidth();
        Loaded += (_, _) => FitToWidth(force: true);
    }

    public InspectionResult? Result
    {
        get => (InspectionResult?)GetValue(ResultProperty);
        set => SetValue(ResultProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public string? GroupDescription
    {
        get => (string?)GetValue(GroupDescriptionProperty);
        set => SetValue(GroupDescriptionProperty, value);
    }

    public string PassDescription
    {
        get => (string)GetValue(PassDescriptionProperty);
        private set => SetValue(PassDescriptionProperty, value);
    }

    public string FailDescription
    {
        get => (string)GetValue(FailDescriptionProperty);
        private set => SetValue(FailDescriptionProperty, value);
    }

    public string NotApplicableDescription
    {
        get => (string)GetValue(NotApplicableDescriptionProperty);
        private set => SetValue(NotApplicableDescriptionProperty, value);
    }

    public bool IsStacked
    {
        get => (bool)GetValue(IsStackedProperty);
        private set => SetValue(IsStackedProperty, value);
    }

    public static string Describe(InspectionResult option, InspectionResult? selected) =>
        InspectionItemViewModel.ResultLabel(option) + (option == selected ? ", selected" : ", not selected");

    public static AnswerLook LookFor(InspectionResult option, bool selected) => (option, selected) switch
    {
        (InspectionResult.Pass, true) => new AnswerLook("FsPassBrush", "FsTeal800Brush", "FsShadowAnswerPass", "FsOnDark", ShowsGloss: true),
        (InspectionResult.Fail, true) => new AnswerLook("FsChipFailBrush", "FsDangerBrush", "FsShadowAnswerFail", "FsDangerStrong", ShowsGloss: false),
        (InspectionResult.NotApplicable, true) => new AnswerLook("FsBannerPausedTileBrush", "FsMutedBrush", "FsShadowNone", "FsInk", ShowsGloss: false),
        _ => new AnswerLook("FsSurfaceBrush", "FsSecondaryStrokeBrush", "FsShadowAnswerIdle", "FsInk2", ShowsGloss: false),
    };

    public static double OptionInnerWidth(double width, bool stacked)
    {
        var option = stacked ? width : (width - (2 * OptionSpacing)) / 3;
        return option - (2 * OptionPadding) - (2 * OptionStroke);
    }

    public static bool ShowsIcon(double innerWidth, double labelWidth) => innerWidth >= IconSize + IconGap + labelWidth;

    public static bool Stacks(double rowInnerWidth, double widestLabelWidth) => rowInnerWidth < widestLabelWidth;

    private void FitToWidth(bool force = false)
    {
        if (Options is null || Width <= 0 || PassLabel.Handler is null)
        {
            return;
        }

        if (!force && Math.Abs(_measuredFor - Width) < 0.5)
        {
            return;
        }

        _measuredFor = Width;
        var labels = new[] { PassLabel, FailLabel, NotApplicableLabel };
        var widths = labels.Select(label => label.Measure(double.PositiveInfinity, double.PositiveInfinity).Width).ToArray();
        var stacked = Stacks(OptionInnerWidth(Width, stacked: false), widths.Max());
        var inner = OptionInnerWidth(Width, stacked);
        var showsIcons = ShowsIcon(inner, widths.Max());
        PassIcon.IsVisible = showsIcons;
        FailIcon.IsVisible = showsIcons;
        NotApplicableIcon.IsVisible = showsIcons;
        if (stacked == IsStacked && Options.ColumnDefinitions.Count == (stacked ? 1 : 3))
        {
            return;
        }

        IsStacked = stacked;
        Options.ColumnDefinitions = stacked ? [new ColumnDefinition(GridLength.Star)] : [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)];
        Options.RowDefinitions = stacked ? [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)] : [new RowDefinition(GridLength.Auto)];
        var options = new[] { PassOption, FailOption, NotApplicableOption };
        for (var index = 0; index < options.Length; index++)
        {
            Grid.SetColumn(options[index], stacked ? 0 : index);
            Grid.SetRow(options[index], stacked ? index : 0);
        }
    }

    private void ApplyLook()
    {
        PassDescription = Describe(InspectionResult.Pass, Result);
        FailDescription = Describe(InspectionResult.Fail, Result);
        NotApplicableDescription = Describe(InspectionResult.NotApplicable, Result);
        if (PassSurface is null)
        {
            return;
        }

        Apply(InspectionResult.Pass, PassSurface, PassIcon, PassLabel);
        Apply(InspectionResult.Fail, FailSurface, FailIcon, FailLabel);
        Apply(InspectionResult.NotApplicable, NotApplicableSurface, NotApplicableIcon, NotApplicableLabel);
        PassGloss.IsVisible = LookFor(InspectionResult.Pass, Result == InspectionResult.Pass).ShowsGloss;
    }

    private void Apply(InspectionResult option, Border surface, Icon icon, Label label)
    {
        var look = LookFor(option, Result == option);
        surface.SetDynamicResource(Border.BackgroundProperty, look.Fill);
        surface.SetDynamicResource(Border.StrokeProperty, look.Stroke);
        surface.SetDynamicResource(ShadowProperty, look.Shadow);
        label.SetDynamicResource(Label.TextColorProperty, look.Text);
        icon.SetDynamicResource(Icon.ColorProperty, look.Text);
    }
}

public sealed record AnswerLook(string Fill, string Stroke, string Shadow, string Text, bool ShowsGloss);
