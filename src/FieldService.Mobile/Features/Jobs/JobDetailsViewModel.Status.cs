using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Shared.Formatting;
using FieldService.Mobile.Shared.Http;
using FieldService.Mobile.Shared.Navigation;
using FieldService.Mobile.Shared.Screens;
using Microsoft.Extensions.Logging;

namespace FieldService.Mobile.Features.Jobs;

public sealed record PageBanner(BannerVariant Variant, string Title, string? Message, string? Reference);

public sealed partial class JobDetailsViewModel
{
    public const string SheetTitle = "Update status";
    public const string UpdateStatusText = "Update status";
    public const string StartHelper = "Dispatch sees the job as In progress as soon as you start.";
    public const string StartOfflineHelper = "You're offline. Connect to start the job.";
    public const string UpdateOfflineHelper = "You're offline. Connect to update the status.";
    public const string ReasonLabel = "Reason for hold";
    public const string ReasonHelp = "Dispatch sees this reason in the job's status history.";
    public const string ReasonRequired = "Enter a reason for putting the job on hold.";
    public const string UnreachableTitle = "FieldService could not be reached.";
    public const string TooManyRequestsTitle = "Too many requests.";
    public const string TryAgainSoon = "Try again in a moment.";
    public const string ConcurrencyConflictCode = "concurrency.conflict";
    public const string InvalidTransitionCode = "job.invalid-transition";
    public const int ReasonMaxLength = 500;
    public const int ReasonCountFrom = 400;

    private CancellationTokenSource? _command;
    private bool _leftLayout;
    private bool _paneMovePending;

    public event EventHandler? StatusChanged;

    [ObservableProperty]
    public partial bool IsStarting { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReasonReadOnly))]
    public partial bool IsUpdatingStatus { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPhoneSheet), nameof(ShowsPaneSheet))]
    public partial bool IsStatusSheetOpen { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<StatusOption> StatusOptions { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsHoldReason), nameof(ConfirmText), nameof(ConfirmBusyText), nameof(ConfirmIconKey))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmStatusCommand))]
    public partial StatusOption? SelectedStatusOption { get; private set; }

    [ObservableProperty]
    public partial string HoldReason { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? HoldReasonError { get; private set; }

    [ObservableProperty]
    public partial int HoldReasonFocusRequest { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSheetFailure))]
    public partial ScreenError? SheetFailure { get; private set; }

    [ObservableProperty]
    public partial int SheetScrollRequest { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActionBanner))]
    public partial PageBanner? ActionBanner { get; private set; }

    [ObservableProperty]
    public partial int ScrollToTopRequest { get; private set; }

    [ObservableProperty]
    public partial int UpdateStatusFocusRequest { get; private set; }

    [ObservableProperty]
    public partial int StartJobFocusRequest { get; private set; }

    [ObservableProperty]
    public partial int TitleFocusRequest { get; private set; }

    [ObservableProperty]
    public partial int StartFocusRequest { get; private set; }

    public IReadOnlyList<AllowedStatusChange> AllowedChanges => Job?.Affordances.AllowedStatusChanges ?? [];

    public bool ShowsStartJob => Job is { Status: JobStatus.Assigned } && AllowedChanges.Any(change => change.Status == JobStatus.InProgress);

    public bool ShowsUpdateStatus => Job is { Status: not JobStatus.Assigned } job && OptionsFor(job, false).Count > 0;

    public bool ShowsNavigationRow => IsAssigned;

    public bool ShowsCallAction => ShowsNavigationRow && CanCall;

    public bool ShowsPhoneSheet => IsStatusSheetOpen && !IsInPane;

    public bool ShowsPaneSheet => IsStatusSheetOpen && IsInPane;

    public bool IsStatusOffline => !IsOnline;

    public bool CanUpdateStatus => CanOpenStatusSheet();

    public string? DockHelper => ShowsStartJob
        ? IsStatusOffline ? StartOfflineHelper : StartHelper
        : ShowsUpdateStatus && IsStatusOffline ? UpdateOfflineHelper : null;

    public string StartJobLabel => IsStarting ? "Starting…" : "Start job";

    public string? ReasonLine => ReasonNote is { } note ? "“" + note + "”" : null;

    public bool HasReasonLine => ReasonLine is not null;

    public string? StatusDescription => Job is { Status: JobStatus.Cancelled } && HasStatusLine
        ? Spoken(StatusLine, ReasonNote)
        : null;

    public string? PaneStatusDescription => StatusDescription is null
        ? null
        : Spoken(StatusLine + ", " + CustomerName, ReasonNote);

    public bool HasStatusDescription => StatusDescription is not null;

    private string? ReasonNote => Job is { } job ? ReasonNoteFor(job) : null;

    public bool ShowsHoldReason => SelectedStatusOption?.RequiresNote == true;

    public bool IsReasonReadOnly => IsUpdatingStatus;

    public string ConfirmText => SelectedStatusOption?.ConfirmText ?? UpdateStatusText;

    public string? ConfirmBusyText => SelectedStatusOption?.BusyText;

    public string? ConfirmIconKey => SelectedStatusOption?.ConfirmIconKey;

    public bool HasSheetFailure => SheetFailure is not null;

    public bool HasActionBanner => ActionBanner is not null;

    public string SheetSubtitle => Job is null ? string.Empty : JobNumber + DisplayFormats.Separator + Title;

    public static IReadOnlyList<StatusOption> OptionsFor(JobDetailsResponse job, bool hasUnsavedAnswers)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (job.Status == JobStatus.Assigned)
        {
            return [];
        }

        IEnumerable<StatusOption> options = job.Affordances.AllowedStatusChanges
            .Select(change => StatusOption.For(job.Status, change))
            .OfType<StatusOption>()
            .Where(option => option.Action != StatusAction.Start);
        if (job.Status is JobStatus.InProgress or JobStatus.OnHold)
        {
            options = options.Append(StatusOption.Complete(job.Affordances.CanSubmit, hasUnsavedAnswers));
        }

        return [.. options];
    }

    public static string? ReasonNoteFor(JobDetailsResponse job)
    {
        ArgumentNullException.ThrowIfNull(job);

        JobHistoryAction? action = job.Status switch
        {
            JobStatus.OnHold => JobHistoryAction.PutOnHold,
            JobStatus.Cancelled => JobHistoryAction.Cancelled,
            _ => null,
        };
        if (action is null)
        {
            return null;
        }

        var note = job.History.LastOrDefault(entry => entry.Action == action)?.Note;
        return string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public static StatusOption? InitialSelection(IReadOnlyList<StatusOption> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var enabled = options.Where(option => option.IsAvailable).ToList();
        return enabled.Count == 1 ? enabled[0] : null;
    }

    public bool CloseSheet()
    {
        if (!IsStatusSheetOpen)
        {
            return false;
        }

        CloseStatusSheet();
        return true;
    }

    public void LeaveLayout()
    {
        if (IsUpdatingStatus)
        {
            _leftLayout = true;
            return;
        }

        IsStatusSheetOpen = false;
        SheetFailure = null;
        HoldReason = string.Empty;
        HoldReasonError = null;
    }

    private void OnWindowLayoutChanged(object? sender, EventArgs e)
    {
        var wasOpen = IsStatusSheetOpen;
        LeaveLayout();
        if (_layout.IsTwoPane && !IsInPane)
        {
            _paneMovePending = true;
            if (!IsUpdatingStatus && !IsStarting)
            {
                _ = MoveToPaneAsync();
            }

            return;
        }

        _paneMovePending = false;
        if (wasOpen && !IsStatusSheetOpen)
        {
            StartFocusRequest++;
        }
    }

    private bool FinishLeftLayout()
    {
        if (!_leftLayout)
        {
            return false;
        }

        _leftLayout = false;
        IsStatusSheetOpen = false;
        SheetFailure = null;
        HoldReason = string.Empty;
        HoldReasonError = null;
        if (!IsInPane && !_paneMovePending)
        {
            StartFocusRequest++;
        }

        return true;
    }

    private async Task MoveToPaneAsync()
    {
        if (!_paneMovePending || IsInPane || !_layout.IsTwoPane || PaneHandoff is not { } handoff)
        {
            _paneMovePending = false;
            return;
        }

        _paneMovePending = false;
        try
        {
            await _navigator.GoToAsync(Routes.Back, new Dictionary<string, object> { [Routes.PaneJobParameter] = handoff }, animate: false);
        }
        catch (Exception exception)
        {
            LogNavigateFailed(_logger, _jobId, exception.GetType().Name);
        }
    }

    private bool CanStartJob() => ShowsStartJob && !IsStatusOffline;

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanStartJob))]
    private async Task StartJobAsync()
    {
        if (IsStarting || IsUpdatingStatus || Job is not { } job)
        {
            return;
        }

        var change = job.Affordances.AllowedStatusChanges.FirstOrDefault(candidate => candidate.Status == JobStatus.InProgress);
        if (change is null)
        {
            return;
        }

        IsStarting = true;
        OnPropertyChanged(nameof(StartJobLabel));
        try
        {
            await ChangeStatusAsync(StatusOption.Start(change.RequiresNote), null, fromSheet: false);
        }
        finally
        {
            IsStarting = false;
            OnPropertyChanged(nameof(StartJobLabel));
        }
    }

    private bool CanOpenStatusSheet() => ShowsUpdateStatus && !IsStatusOffline;

    [RelayCommand(CanExecute = nameof(CanOpenStatusSheet))]
    private void OpenStatusSheet()
    {
        if (Job is not { } job || IsUpdatingStatus)
        {
            return;
        }

        var options = OptionsFor(job, UnsavedAnswers > 0);
        if (options.Count == 0)
        {
            return;
        }

        var selected = InitialSelection(options);
        foreach (var option in options)
        {
            option.IsSelected = ReferenceEquals(option, selected);
        }

        StatusOptions = options;
        SelectedStatusOption = selected;
        SheetFailure = null;
        HoldReasonError = null;
        IsStatusSheetOpen = true;
    }

    [RelayCommand]
    private void SelectStatusOption(StatusOption? option)
    {
        if (IsUpdatingStatus || option is null || !StatusOptions.Contains(option) || !option.IsAvailable)
        {
            return;
        }

        foreach (var candidate in StatusOptions)
        {
            candidate.IsSelected = ReferenceEquals(candidate, option);
        }

        SelectedStatusOption = option;
    }

    [RelayCommand]
    private void CloseStatusSheet()
    {
        if (IsUpdatingStatus || !IsStatusSheetOpen)
        {
            return;
        }

        IsStatusSheetOpen = false;
        if (CanUpdateStatus || FocusesDisabledTrigger(_deviceInfo.Platform))
        {
            UpdateStatusFocusRequest++;
        }
        else
        {
            TitleFocusRequest++;
        }
    }

    public static bool FocusesDisabledTrigger(DevicePlatform platform) => platform != DevicePlatform.WinUI;

    private bool CanConfirmStatus() => SelectedStatusOption is not null;

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanConfirmStatus))]
    private async Task ConfirmStatusAsync()
    {
        if (IsUpdatingStatus || IsStarting || SelectedStatusOption is not { } option || Job is not { } job)
        {
            return;
        }

        if (option.IsComplete)
        {
            if (!option.IsAvailable)
            {
                return;
            }

            IsStatusSheetOpen = false;
            try
            {
                await _navigator.GoToAsync(Routes.ReviewPage, new Dictionary<string, object>
                {
                    [Routes.JobIdParameter] = job.Id,
                    [Routes.SourceParameter] = Routes.FromSheet,
                });
            }
            catch (Exception exception)
            {
                LogNavigateFailed(_logger, _jobId, exception.GetType().Name);
            }

            return;
        }

        string? note = null;
        if (option.RequiresNote)
        {
            if (string.IsNullOrWhiteSpace(HoldReason))
            {
                HoldReasonError = ReasonRequired;
                HoldReasonFocusRequest++;
                return;
            }

            note = HoldReason.Trim();
        }

        SheetFailure = null;
        IsUpdatingStatus = true;
        try
        {
            await ChangeStatusAsync(option, note, fromSheet: true);
        }
        finally
        {
            IsUpdatingStatus = false;
        }
    }

    partial void OnHoldReasonChanged(string value)
    {
        if (HoldReasonError is not null && !string.IsNullOrWhiteSpace(value))
        {
            HoldReasonError = null;
        }
    }

    private async Task ChangeStatusAsync(StatusOption option, string? note, bool fromSheet)
    {
        if (Job is not { } job)
        {
            return;
        }

        _command?.Cancel();
        var command = new CancellationTokenSource();
        _command = command;
        var token = command.Token;
        var requestedFor = _session.User?.Id;
        if (IsStatusOffline)
        {
            ShowStatusFailure(new ScreenError(UnreachableTitle, CheckConnectionMessage, null), fromSheet);
            return;
        }

        try
        {
            var updated = await _jobs.UpdateStatusAsync(job.Id, new UpdateJobStatusRequest(option.Status, note, job.Version), token);
            if (Dropped(token, requestedFor))
            {
                DropStatusChange(token);
                return;
            }

            ShowUpdatedJob(updated);
            IsStatusSheetOpen = false;
            SheetFailure = null;
            if (option.RequiresNote)
            {
                HoldReason = string.Empty;
                HoldReasonError = null;
            }

            LogStatusChanged(_logger, job.Id, option.Status);
            _screenReader.Announce(option.Announcement(JobNumber));
            if (!FinishLeftLayout())
            {
                UpdateStatusFocusRequest++;
            }

            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception) when (Dropped(token, requestedFor))
        {
            DropStatusChange(token);
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            LogStatusChangeFailed(_logger, job.Id, option.Status, 404);
            IsStatusSheetOpen = false;
            FinishLeftLayout();
            await NoLongerAssignedAsync();
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            LogStatusChangeFailed(_logger, job.Id, option.Status, 409);
            IsStatusSheetOpen = false;
            if (FinishLeftLayout())
            {
                await LoadJobAsync();
            }
            else
            {
                await ExplainConflictAsync(option, exception.Code);
            }

            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (ApiException exception) when (fromSheet && option.RequiresNote && !_leftLayout && exception.StatusCode == HttpStatusCode.BadRequest && exception.Problem?.FirstError("note") is { } message)
        {
            LogStatusChangeFailed(_logger, job.Id, option.Status, 400);
            HoldReasonError = message;
            HoldReasonFocusRequest++;
        }
        catch (Exception exception) when (IsFailure(exception, token))
        {
            LogStatusChangeFailed(_logger, job.Id, option.Status, exception is ApiException api ? (int)api.StatusCode : 0);
            if (!FinishLeftLayout())
            {
                ShowStatusFailure(DescribeStatusFailure(exception, option), fromSheet);
            }
        }
        finally
        {
            if (ReferenceEquals(_command, command))
            {
                _command = null;
            }
        }

        if (_paneMovePending)
        {
            await MoveToPaneAsync();
        }
    }

    private static string Spoken(string line, string? note) => note is null ? line : line + ". " + note;

    private bool Dropped(CancellationToken token, Guid? requestedFor) =>
        token.IsCancellationRequested || (requestedFor is { } requester && _session.User?.Id != requester);

    private void DropStatusChange(CancellationToken token)
    {
        if (!token.IsCancellationRequested)
        {
            Reset();
        }
    }

    private void ShowUpdatedJob(JobDetailsResponse updated)
    {
        _loadedAt = _clock.GetUtcNow();
        Job = updated;
        Timeline = BuildTimeline(updated, Zone, Today);
        Error = null;
        OfflineMessage = null;
        RefreshError = null;
        ActionBanner = null;
        State = ScreenState.Content;
        UpdateStartsLine();
    }

    private async Task ExplainConflictAsync(StatusOption option, string? code)
    {
        ActionBanner = null;
        await LoadJobAsync();
        if (Job is null || State != ScreenState.Content || RefreshError is not null)
        {
            return;
        }

        ActionBanner = code == InvalidTransitionCode
            ? new PageBanner(BannerVariant.Conflict, option.InvalidTransitionTitle(JobNumber), "Its status changed after you opened it. The latest version is shown.", null)
            : new PageBanner(BannerVariant.Conflict, JobNumber + " changed after you opened it.", "The latest version is shown. Check it, then try again.", null);
        ScrollToTopRequest++;
        if (option.Action == StatusAction.Start && ShowsStartJob)
        {
            StartJobFocusRequest++;
        }
        else if (option.Action != StatusAction.Start && ShowsUpdateStatus)
        {
            UpdateStatusFocusRequest++;
        }
        else
        {
            TitleFocusRequest++;
        }
    }

    private void ShowStatusFailure(ScreenError failure, bool fromSheet)
    {
        if (fromSheet)
        {
            SheetFailure = failure;
            SheetScrollRequest++;
            return;
        }

        RefreshError = null;
        ActionBanner = new PageBanner(BannerVariant.Error, failure.Title, failure.Message, failure.Reference);
        ScrollToTopRequest++;
    }

    private static ScreenError DescribeStatusFailure(Exception exception, StatusOption option) => exception switch
    {
        ApiException { StatusCode: HttpStatusCode.TooManyRequests } api => new ScreenError(TooManyRequestsTitle, RetryWait(api.RetryAfter), null),
        ApiException api => new ScreenError(option.FailedTitle, TryAgainSoon, api.CorrelationId),
        _ => new ScreenError(UnreachableTitle, CheckConnectionMessage, null),
    };

    private void ForgetStatusChanges(bool keepDraft)
    {
        _command?.Cancel();
        _command = null;
        _leftLayout = false;
        _paneMovePending = false;
        IsStatusSheetOpen = false;
        IsStarting = false;
        IsUpdatingStatus = false;
        StatusOptions = [];
        SelectedStatusOption = null;
        SheetFailure = null;
        ActionBanner = null;
        HoldReasonError = null;
        if (!keepDraft)
        {
            HoldReason = string.Empty;
        }

        OnPropertyChanged(nameof(StartJobLabel));
    }

    private void NotifyStatusActions()
    {
        OnPropertyChanged(nameof(AllowedChanges));
        OnPropertyChanged(nameof(ShowsStartJob));
        OnPropertyChanged(nameof(ShowsUpdateStatus));
        OnPropertyChanged(nameof(ShowsNavigationRow));
        OnPropertyChanged(nameof(ShowsCallAction));
        OnPropertyChanged(nameof(ShowsPhoneSheet));
        OnPropertyChanged(nameof(ShowsPaneSheet));
        OnPropertyChanged(nameof(ShowsDock));
        OnPropertyChanged(nameof(ShowsHeroActions));
        OnPropertyChanged(nameof(IsStatusOffline));
        OnPropertyChanged(nameof(CanUpdateStatus));
        OnPropertyChanged(nameof(DockHelper));
        OnPropertyChanged(nameof(ReasonLine));
        OnPropertyChanged(nameof(HasReasonLine));
        OnPropertyChanged(nameof(StatusDescription));
        OnPropertyChanged(nameof(PaneStatusDescription));
        OnPropertyChanged(nameof(HasStatusDescription));
        OnPropertyChanged(nameof(SheetSubtitle));
        OnPropertyChanged(nameof(ShowsChecklistLink));
        OnPropertyChanged(nameof(ShowsChecklistRow));
        OnPropertyChanged(nameof(ShowsInspectionAction));
        OnPropertyChanged(nameof(InspectionActionText));
        OnPropertyChanged(nameof(ShowsUpdateStatusPrimary));
        OnPropertyChanged(nameof(ShowsUpdateStatusSecondary));
        NotifyChecklist();
        StartJobCommand.NotifyCanExecuteChanged();
        OpenStatusSheetCommand.NotifyCanExecuteChanged();
    }

    [LoggerMessage(EventName = "jobs.status-changed", Level = LogLevel.Information, Message = "jobs.status-changed {JobId} {Status}")]
    private static partial void LogStatusChanged(ILogger logger, Guid jobId, JobStatus status);

    [LoggerMessage(EventName = "jobs.status-change-failed", Level = LogLevel.Warning, Message = "jobs.status-change-failed {JobId} {Status} {StatusCode}")]
    private static partial void LogStatusChangeFailed(ILogger logger, Guid jobId, JobStatus status, int statusCode);
}
