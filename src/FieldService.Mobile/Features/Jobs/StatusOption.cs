using CommunityToolkit.Mvvm.ComponentModel;

namespace FieldService.Mobile.Features.Jobs;

public enum StatusAction
{
    Start,
    PutOnHold,
    Resume,
}

public sealed partial class StatusOption : ObservableObject
{
    private StatusOption(
        StatusAction action,
        JobStatus status,
        bool requiresNote,
        string title,
        string helper,
        string iconKey,
        string iconColorKey,
        string confirmText,
        string busyText,
        string pastTense)
    {
        Action = action;
        Status = status;
        RequiresNote = requiresNote;
        Title = title;
        Helper = helper;
        IconKey = iconKey;
        IconColorKey = iconColorKey;
        ConfirmText = confirmText;
        BusyText = busyText;
        PastTense = pastTense;
    }

    public StatusAction Action { get; }

    public JobStatus Status { get; }

    public bool RequiresNote { get; }

    public string Title { get; }

    public string Helper { get; }

    public string IconKey { get; }

    public string IconColorKey { get; }

    public string ConfirmText { get; }

    public string BusyText { get; }

    public string PastTense { get; }

    public bool IsAvailable => true;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string FailedTitle => Action switch
    {
        StatusAction.Start => "The job could not be started.",
        StatusAction.PutOnHold => "The job could not be put on hold.",
        _ => "The job could not be resumed.",
    };

    public static StatusOption? For(JobStatus current, AllowedStatusChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return (current, change.Status) switch
        {
            (JobStatus.Assigned, JobStatus.InProgress) => Start(change.RequiresNote),
            (JobStatus.OnHold, JobStatus.InProgress) => new StatusOption(
                StatusAction.Resume,
                JobStatus.InProgress,
                change.RequiresNote,
                "Resume",
                "Carry on where you left off",
                "FsIconPlayCircle",
                "FsStatusInProgress",
                "Resume job",
                "Resuming…",
                "resumed"),
            (_, JobStatus.OnHold) => new StatusOption(
                StatusAction.PutOnHold,
                JobStatus.OnHold,
                change.RequiresNote,
                "Put on hold",
                "Waiting on parts, access or the customer",
                "FsIconPauseCircle",
                "FsStatusOnHold",
                "Put on hold",
                "Putting on hold…",
                "put on hold"),
            _ => null,
        };
    }

    public static StatusOption Start(bool requiresNote = false) => new(
        StatusAction.Start,
        JobStatus.InProgress,
        requiresNote,
        "Start job",
        string.Empty,
        "FsIconPlayCircle",
        "FsStatusInProgress",
        "Start job",
        "Starting…",
        "started");

    public string Announcement(string jobNumber) => jobNumber + " " + PastTense + ".";

    public string InvalidTransitionTitle(string jobNumber) => Action switch
    {
        StatusAction.Start => jobNumber + " can no longer be started.",
        StatusAction.PutOnHold => jobNumber + " can no longer be put on hold.",
        _ => jobNumber + " can no longer be resumed.",
    };

    public override string ToString() => $"{nameof(StatusOption)} {Status}";
}
