using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Formatting;
using Microsoft.Maui.Controls.Shapes;

namespace FieldService.Mobile.Shared.Controls;

public partial class StatusChip : ContentView
{
    public static readonly BindableProperty StatusProperty = BindableProperty.Create(
        nameof(Status),
        typeof(JobStatus),
        typeof(StatusChip),
        JobStatus.Unknown,
        propertyChanged: (bindable, _, _) => ((StatusChip)bindable).Apply());

    public static readonly BindableProperty IsCompactProperty = BindableProperty.Create(
        nameof(IsCompact),
        typeof(bool),
        typeof(StatusChip),
        false,
        propertyChanged: (bindable, _, _) => ((StatusChip)bindable).Apply());

    public static readonly BindableProperty LabelProperty = BindableProperty.Create(nameof(Label), typeof(string), typeof(StatusChip), string.Empty);

    public StatusChip()
    {
        InitializeComponent();
        Apply();
    }

    public JobStatus Status
    {
        get => (JobStatus)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
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

    public static string Description(JobStatus status) => "Status: " + DisplayFormats.StatusLabel(status);

    public static (string Fill, string Ring, string Text, string Icon) ResourceKeys(JobStatus status) => status switch
    {
        JobStatus.Unassigned => ("FsChipUnassignedBrush", "FsStatusUnassignedRingBrush", "FsStatusUnassigned", "FsIconCircleDashed"),
        JobStatus.Assigned => ("FsChipAssignedBrush", "FsStatusAssignedRingBrush", "FsStatusAssigned", "FsIconUserCheck"),
        JobStatus.InProgress => ("FsChipInProgressBrush", "FsStatusInProgressRingBrush", "FsStatusInProgress", "FsIconPlayCircle"),
        JobStatus.OnHold => ("FsChipOnHoldBrush", "FsStatusOnHoldRingBrush", "FsStatusOnHold", "FsIconPauseCircle"),
        JobStatus.Completed => ("FsChipCompletedBrush", "FsStatusCompletedRingBrush", "FsStatusCompleted", "FsIconCheckCircle"),
        JobStatus.Cancelled => ("FsChipCancelledBrush", "FsStatusCancelledRingBrush", "FsStatusCancelled", "FsIconXCircle"),
        _ => ("FsChipCancelledBrush", "FsStatusCancelledRingBrush", "FsStatusCancelled", "FsIconCircleDashed"),
    };

    private void Apply()
    {
        Label = DisplayFormats.StatusLabel(Status);
        SemanticProperties.SetDescription(this, Description(Status));
        if (Chip is null)
        {
            return;
        }

        var (fill, ring, text, icon) = ResourceKeys(Status);
        Chip.SetDynamicResource(Border.BackgroundProperty, fill);
        Chip.SetDynamicResource(Border.StrokeProperty, ring);
        Chip.StrokeDashArray = Status == JobStatus.Unassigned ? [3, 3] : null;
        Chip.StrokeShape = new RoundRectangle { CornerRadius = IsCompact ? 13 : 16 };
        Chip.SetDynamicResource(Border.PaddingProperty, IsCompact ? "FsPaddingChipCompact" : "FsPaddingChip");
        Chip.SetDynamicResource(MinimumHeightRequestProperty, IsCompact ? "FsHeightChipCompact" : "FsHeightChip");
        ChipLabel.SetDynamicResource(Microsoft.Maui.Controls.Label.TextColorProperty, text);
        ChipLabel.SetDynamicResource(Microsoft.Maui.Controls.Label.FontSizeProperty, IsCompact ? "FsFontSizeChipCompact" : "FsFontSizeChip");
        ChipIcon.SetDynamicResource(Icon.PathDataProperty, icon);
        ChipIcon.SetDynamicResource(Icon.ColorProperty, text);
        ChipIcon.Size = IsCompact ? 14 : 16;
        ChipIcon.IsDashed = Status == JobStatus.Unassigned;
    }
}
