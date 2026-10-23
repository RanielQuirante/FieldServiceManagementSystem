using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Formatting;
using Microsoft.Maui.Controls.Shapes;

namespace FieldService.Mobile.Shared.Controls;

public partial class JobCard : ContentView, ITappableCard
{
    public static readonly BindableProperty JobNumberProperty = BindableProperty.Create(nameof(JobNumber), typeof(string), typeof(JobCard));

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(JobCard));

    public static readonly BindableProperty StatusProperty = BindableProperty.Create(nameof(Status), typeof(JobStatus), typeof(JobCard), JobStatus.Unknown);

    public static readonly BindableProperty PriorityProperty = BindableProperty.Create(
        nameof(Priority),
        typeof(JobPriority),
        typeof(JobCard),
        JobPriority.Unknown,
        propertyChanged: (bindable, _, _) => ((JobCard)bindable).ApplyLook());

    public static readonly BindableProperty CustomerNameProperty = BindableProperty.Create(nameof(CustomerName), typeof(string), typeof(JobCard));

    public static readonly BindableProperty SiteAddressProperty = BindableProperty.Create(nameof(SiteAddress), typeof(string), typeof(JobCard));

    public static readonly BindableProperty TimeWindowProperty = BindableProperty.Create(
        nameof(TimeWindow),
        typeof(string),
        typeof(JobCard),
        propertyChanged: (bindable, _, _) => ((JobCard)bindable).OnTimeWindowChanged());

    public static readonly BindableProperty WellTimeProperty = BindableProperty.Create(nameof(WellTime), typeof(string), typeof(JobCard));

    public static readonly BindableProperty DescriptionProperty = BindableProperty.Create(
        nameof(Description),
        typeof(string),
        typeof(JobCard),
        propertyChanged: (bindable, _, _) => ((JobCard)bindable).ApplyLook());

    public static readonly BindableProperty IsCompactProperty = BindableProperty.Create(
        nameof(IsCompact),
        typeof(bool),
        typeof(JobCard),
        false,
        propertyChanged: (bindable, _, _) => ((JobCard)bindable).ApplyLook());

    public static readonly BindableProperty IsLoadingProperty = BindableProperty.Create(
        nameof(IsLoading),
        typeof(bool),
        typeof(JobCard),
        false,
        propertyChanged: (bindable, _, _) => ((JobCard)bindable).ApplyLook());

    public static readonly BindableProperty IsSelectedProperty = BindableProperty.Create(
        nameof(IsSelected),
        typeof(bool),
        typeof(JobCard),
        false,
        propertyChanged: (bindable, _, _) => ((JobCard)bindable).ApplyLook());

    public static readonly BindableProperty ShowsContentProperty = BindableProperty.Create(nameof(ShowsContent), typeof(bool), typeof(JobCard), true);

    public static readonly BindableProperty ShowsSelectionProperty = BindableProperty.Create(nameof(ShowsSelection), typeof(bool), typeof(JobCard), false);

    public const double ClockWidth = 16;
    public const double ClockGap = 8;
    public const double ChevronWidth = 20;
    public const double WellGap = 10;
    public const double WellRowGap = 4;

    public JobCard()
    {
        InitializeComponent();
        ApplyWellRows(twoRows: false);
        Well.SizeChanged += (_, _) => UpdateWellRows();
        ApplyLook();
    }

    public bool IsTwoRowWell { get; private set; }

    public string? JobNumber
    {
        get => (string?)GetValue(JobNumberProperty);
        set => SetValue(JobNumberProperty, value);
    }

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public JobStatus Status
    {
        get => (JobStatus)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public JobPriority Priority
    {
        get => (JobPriority)GetValue(PriorityProperty);
        set => SetValue(PriorityProperty, value);
    }

    public string? CustomerName
    {
        get => (string?)GetValue(CustomerNameProperty);
        set => SetValue(CustomerNameProperty, value);
    }

    public string? SiteAddress
    {
        get => (string?)GetValue(SiteAddressProperty);
        set => SetValue(SiteAddressProperty, value);
    }

    public string? TimeWindow
    {
        get => (string?)GetValue(TimeWindowProperty);
        set => SetValue(TimeWindowProperty, value);
    }

    public string? WellTime
    {
        get => (string?)GetValue(WellTimeProperty);
        private set => SetValue(WellTimeProperty, value);
    }

    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public bool ShowsContent
    {
        get => (bool)GetValue(ShowsContentProperty);
        private set => SetValue(ShowsContentProperty, value);
    }

    public bool ShowsSelection
    {
        get => (bool)GetValue(ShowsSelectionProperty);
        private set => SetValue(ShowsSelectionProperty, value);
    }

    public bool ActsAsButton => TappableCard.ActsAsButton(IsLoading, Description);

    public static string MetaStyleKey(bool isCompact) => isCompact ? "FsTextCompactMeta" : "FsTextCardMeta";

    public static double TimeGroupWidth(double timeWidth) => ClockWidth + ClockGap + timeWidth;

    public static bool WellNeedsTwoRows(double timeGroupWidth, double priorityWidth, double contentWidth) =>
        timeGroupWidth > contentWidth - priorityWidth - ChevronWidth - (2 * WellGap);

    public static WellRows WellRowsFor(bool twoRows) => twoRows
        ? new WellRows(TimeColumnSpan: 2, PriorityRow: 1, PriorityColumn: 0, PriorityIndent: ClockWidth + ClockGap, RowSpacing: WellRowGap, PaddingKey: "FsPaddingWellTwoRow", PriorityColumnIsAuto: false)
        : new WellRows(TimeColumnSpan: 1, PriorityRow: 0, PriorityColumn: 1, PriorityIndent: 0, RowSpacing: 0, PaddingKey: "FsPaddingWell", PriorityColumnIsAuto: true);

    private void ApplyWellRows(bool twoRows)
    {
        var rows = WellRowsFor(twoRows);
        IsTwoRowWell = twoRows;
        WellGrid.ColumnDefinitions[1].Width = rows.PriorityColumnIsAuto ? GridLength.Auto : new GridLength(0);
        Grid.SetColumnSpan(TimeGroup, rows.TimeColumnSpan);
        Grid.SetRow(WellPriority, rows.PriorityRow);
        Grid.SetColumn(WellPriority, rows.PriorityColumn);
        WellPriority.Margin = new Thickness(rows.PriorityIndent, 0, 0, 0);
        WellGrid.RowSpacing = rows.RowSpacing;
        Well.SetDynamicResource(Border.PaddingProperty, rows.PaddingKey);
    }

    private void OnTimeWindowChanged()
    {
        WellTime = DisplayFormats.BreakAfterEnDash(TimeWindow);
        ScheduleWellRows();
    }

    private void ScheduleWellRows()
    {
        if (Dispatcher is { } dispatcher)
        {
            dispatcher.Dispatch(UpdateWellRows);
        }
    }

    private void UpdateWellRows()
    {
        if (Well is null || Well.Width <= 0 || TimeLabel.Handler is null || WellPriority.Handler is null)
        {
            return;
        }

        var contentWidth = Well.Width - Well.Padding.HorizontalThickness;
        var timeGroup = TimeGroupWidth(TimeLabel.Measure(double.PositiveInfinity, double.PositiveInfinity).Width);
        var priority = WellPriority.Measure(double.PositiveInfinity, double.PositiveInfinity).Width - WellPriority.Margin.HorizontalThickness;
        ((IView)TimeLabel).InvalidateMeasure();
        ((IView)WellPriority).InvalidateMeasure();
        var twoRows = WellNeedsTwoRows(timeGroup, priority, contentWidth);
        if (twoRows != IsTwoRowWell)
        {
            ApplyWellRows(twoRows);
        }
    }

    private void ApplyLook()
    {
        ShowsContent = !IsLoading;
        ShowsSelection = IsCompact && IsSelected && !IsLoading;
        AutomationProperties.SetIsInAccessibleTree(this, !IsLoading);
        SemanticProperties.SetDescription(this, IsLoading ? null : Description);
        if (Card is null)
        {
            return;
        }

        var urgent = Priority == JobPriority.Urgent;
        Card.SetDynamicResource(Border.BackgroundProperty, urgent ? "FsCardUrgentBrush" : "FsGlassBrush");
        Card.SetDynamicResource(Border.StrokeProperty, ShowsSelection ? "FsTeal700Brush" : urgent ? "FsAccentBrush" : "FsGlassStrokeBrush");
        Card.StrokeThickness = ShowsSelection ? 2 : urgent ? 1.5 : 1;
        Card.SetDynamicResource(Border.ShadowProperty, urgent ? "FsShadowUrgentCard" : "FsShadowGlass");
        Card.StrokeShape = new RoundRectangle { CornerRadius = IsCompact ? 18 : 22 };
        Card.Padding = IsCompact ? new Thickness(6) : new Thickness(8);
        CardStack.Spacing = IsCompact ? 8 : 12;
        Summary.Spacing = IsCompact ? 8 : 10;
        TitleLabel.SetDynamicResource(StyleProperty, IsCompact ? "FsTextCompactTitle" : "FsTextSection");
        var meta = MetaStyleKey(IsCompact);
        CustomerLabel.SetDynamicResource(StyleProperty, meta);
        CustomerLineBox.SetDynamicResource(StyleProperty, meta);
        SiteLabel.SetDynamicResource(StyleProperty, meta);
        SiteLineBox.SetDynamicResource(StyleProperty, meta);
        Well.SetDynamicResource(Border.BackgroundProperty, urgent ? "FsWellUrgentBrush" : "FsWellBrush");
        Well.SetDynamicResource(MinimumHeightRequestProperty, IsCompact ? "FsHeightWellCompact" : "FsHeightWell");
        Well.StrokeShape = new RoundRectangle { CornerRadius = IsCompact ? 12 : 14 };
        ScheduleWellRows();
    }
}

public sealed record WellRows(int TimeColumnSpan, int PriorityRow, int PriorityColumn, double PriorityIndent, double RowSpacing, string PaddingKey, bool PriorityColumnIsAuto);
