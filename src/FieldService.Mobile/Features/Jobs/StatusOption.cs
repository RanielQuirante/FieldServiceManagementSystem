using CommunityToolkit.Mvvm.ComponentModel;

namespace FieldService.Mobile.Features.Jobs;

public enum StatusAction
{
    Start,
    PutOnHold,
    Resume,
    Complete,
}

public sealed partial class StatusOption : ObservableObject
{
    public const string CompleteTitle = "Complete job";
    public const string CompleteAllowedHelper = "Review the checklist and submit the job";
    public const string CompleteIncompleteHelper = "Finish the inspection checklist first";
    public const string CompleteResumeHelper = "Resume the job first";
    public const string CompleteUnsavedHelper = "Some checklist answers are not saved yet";
    public const string CompleteUnknownHelper = "Not available right now";
    public const string CompleteConfirmText = "Review and submit";

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
        string pastTense,
        bool isAvailable = true)
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
        IsAvailable = isAvailable;
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

    public bool IsAvailable { get; }

    public bool IsComplete => Action == StatusAction.Complete;

    public string ConfirmIconKey => IsComplete ? "FsIconClipboardCheck" : IconKey;

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

    public static StatusOption Complete(SubmitAffordance canSubmit, bool hasUnsavedAnswers)
    {
        ArgumentNullException.ThrowIfNull(canSubmit);

        var available = canSubmit.Allowed && !hasUnsavedAnswers;
        var helper = hasUnsavedAnswers
            ? CompleteUnsavedHelper
            : canSubmit.Allowed
                ? CompleteAllowedHelper
                : canSubmit.BlockingCode switch
                {
                    SubmitAffordance.IncompleteCode => CompleteIncompleteHelper,
                    SubmitAffordance.InvalidTransitionCode => CompleteResumeHelper,
                    _ => CompleteUnknownHelper,
                };
        return new StatusOption(
            StatusAction.Complete,
            JobStatus.Completed,
            false,
            CompleteTitle,
            helper,
            "FsIconCheckCircle",
            "FsStatusCompleted",
            CompleteConfirmText,
            string.Empty,
            "submitted",
            available);
    }

    public string Announcement(string jobNumber) => jobNumber + " " + PastTense + ".";

    public string InvalidTransitionTitle(string jobNumber) => Action switch
    {
        StatusAction.Start => jobNumber + " can no longer be started.",
        StatusAction.PutOnHold => jobNumber + " can no longer be put on hold.",
        _ => jobNumber + " can no longer be resumed.",
    };

    public override string ToString() => $"{nameof(StatusOption)} {Status}";
}
