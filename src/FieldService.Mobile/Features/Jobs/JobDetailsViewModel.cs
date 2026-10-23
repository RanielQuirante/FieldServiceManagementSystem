using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FieldService.Mobile.Features.Inspection;
using FieldService.Mobile.Shared.Connectivity;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Shared.Formatting;
using FieldService.Mobile.Shared.Http;
using FieldService.Mobile.Shared.Navigation;
using FieldService.Mobile.Shared.Screens;
using FieldService.Mobile.Shared.Session;
using Microsoft.Extensions.Logging;

namespace FieldService.Mobile.Features.Jobs;

public sealed partial class JobDetailsViewModel : ScreenViewModel, IQueryAttributable
{
    public const string PageTitle = "Job details";
    public const string LoadFailedTitle = "The job could not be loaded.";
    public const string RefreshFailedTitle = "The job could not be refreshed.";
    public const string OfflineTitle = "You're offline.";
    public const string MapSearchAddress = "https://www.bing.com/maps?q=";
    public const string StartInspectionText = "Start inspection";
    public const string ContinueInspectionText = "Continue inspection";
    public const string NotSubmittedSuffix = " · not submitted";

    public static readonly TimeSpan MetaRefreshInterval = TimeSpan.FromMinutes(1);

    private readonly JobsApiClient _jobs;
    private readonly InspectionStore _inspections;
    private readonly ISessionService _session;
    private readonly IMap _map;
    private readonly IPhoneDialer _phoneDialer;
    private readonly ILauncher _launcher;
    private readonly INavigator _navigator;
    private readonly ISemanticScreenReader _screenReader;
    private readonly TimeProvider _clock;
    private readonly ILogger<JobDetailsViewModel> _logger;
    private CancellationTokenSource? _load;
    private ITimer? _timer;
    private SynchronizationContext? _context;
    private DateTimeOffset? _loadedAt;
    private Guid _jobId;
    private string? _pendingNotice;

    public JobDetailsViewModel(
        JobsApiClient jobs,
        InspectionStore inspections,
        ISessionService session,
        IMap map,
        IPhoneDialer phoneDialer,
        ILauncher launcher,
        INavigator navigator,
        ISemanticScreenReader screenReader,
        NetworkStatus network,
        TimeProvider clock,
        ILogger<JobDetailsViewModel> logger)
        : base(network)
    {
        _jobs = jobs;
        _inspections = inspections;
        _session = session;
        _map = map;
        _phoneDialer = phoneDialer;
        _launcher = launcher;
        _navigator = navigator;
        _screenReader = screenReader;
        _clock = clock;
        _logger = logger;
        Timeline = [];
    }

    public event EventHandler? JobUnavailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(HasJob),
        nameof(JobNumber),
        nameof(Title),
        nameof(Status),
        nameof(Priority),
        nameof(IsAssigned),
        nameof(ScheduleLine),
        nameof(AssignedLine),
        nameof(HasAssignedLine),
        nameof(StatusLine),
        nameof(HasStatusLine),
        nameof(PaneMeta),
        nameof(CustomerName),
        nameof(SiteAddress),
        nameof(AccessNotes),
        nameof(HasAccessNotes),
        nameof(ContactValue),
        nameof(ContactSecondary),
        nameof(HasContact),
        nameof(HasContactSecondary),
        nameof(ContactDescription),
        nameof(ScheduledText),
        nameof(ShowsScheduledRow),
        nameof(Equipment),
        nameof(HasEquipment),
        nameof(ChecklistName),
        nameof(Description),
        nameof(ShowsRowActions),
        nameof(ShowsDock),
        nameof(ShowsHeroActions),
        nameof(CanCall),
        nameof(ShowsRowCall),
        nameof(CallLabel),
        nameof(NavigateLabel),
        nameof(NavigateColumnSpan),
        nameof(ShowsPaneHero),
        nameof(ShowsPaneMeta))]
    [NotifyCanExecuteChangedFor(nameof(NavigateCommand), nameof(CallCommand))]
    public partial JobDetailsResponse? Job { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ShowsDock),
        nameof(ShowsHeroActions),
        nameof(ShowsPushedHeader),
        nameof(ShowsPaneHero),
        nameof(ContentPadding),
        nameof(InfoColumnSpan),
        nameof(DetailsRow),
        nameof(DetailsColumn),
        nameof(DetailsColumnSpan))]
    public partial bool IsInPane { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStartsLine))]
    public partial string? StartsLine { get; private set; }

    [ObservableProperty]
    public partial string? OfflineMessage { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<TimelineEntry> Timeline { get; private set; }

    public Guid JobId => _jobId;

    public bool HasJob => Job is not null;

    public string JobNumber => Job is null ? string.Empty : DisplayFormats.JobNumber(Job.Number);

    public string Title => Job?.Title ?? string.Empty;

    public JobStatus Status => Job?.Status ?? JobStatus.Unknown;

    public JobPriority Priority => Job?.Priority ?? JobPriority.Unknown;

    public bool IsAssigned => Job?.Status == JobStatus.Assigned;

    public bool HasStartsLine => !string.IsNullOrEmpty(StartsLine);

    public string ScheduleLine => Job is null
        ? string.Empty
        : DisplayFormats.ScheduleLine(Job.ScheduledStart, Job.ScheduledEnd, Zone, Today);

    public string AssignedLine
    {
        get
        {
            var assigned = Job?.History.LastOrDefault(entry => entry.Action == JobHistoryAction.Assigned);
            return assigned is null
                ? string.Empty
                : "Assigned to you by " + assigned.Actor.DisplayName + " " + DisplayFormats.AtMoment(assigned.OccurredAt, Zone, Today);
        }
    }

    public bool HasAssignedLine => !string.IsNullOrEmpty(AssignedLine);

    public string StatusLine => Job is null ? string.Empty : BuildStatusLine(Job, Zone, Today);

    public bool HasStatusLine => !string.IsNullOrEmpty(StatusLine);

    public string PaneMeta => HasStatusLine ? StatusLine + DisplayFormats.Separator + CustomerName : CustomerName;

    public string CustomerName => Job?.CustomerName ?? string.Empty;

    public string SiteAddress => Job?.SiteAddress ?? string.Empty;

    public string? AccessNotes => Blank(Job?.SiteAccessNotes);

    public bool HasAccessNotes => AccessNotes is not null;

    public string ContactValue => ContactName ?? ContactPhone ?? string.Empty;

    public string? ContactSecondary => ContactName is null ? null : ContactPhone;

    public bool HasContact => ContactName is not null || ContactPhone is not null;

    public bool HasContactSecondary => ContactSecondary is not null;

    public string ContactDescription => HasContactSecondary ? ContactValue + ", " + ContactSecondary : ContactValue;

    public string ScheduledText => Job is null
        ? string.Empty
        : DisplayFormats.Scheduled(Job.ScheduledStart, Job.ScheduledEnd, Zone, Today);

    public bool ShowsScheduledRow => Job is not null && !IsAssigned;

    public string? Equipment => Blank(Job?.Equipment);

    public bool HasEquipment => Equipment is not null;

    public string ChecklistName => Job?.ChecklistTemplate.Name ?? string.Empty;

    public string Description => Job?.Description ?? string.Empty;

    public bool ShowsRowActions => Job is not null && !IsAssigned;

    public bool ShowsDock => !IsInPane && (IsAssigned || ShowsUpdateStatus || ShowsInspectionAction);

    public bool ShowsHeroActions => IsInPane && (IsAssigned || ShowsUpdateStatus || ShowsInspectionAction);

    public bool ShowsChecklistLink => Job?.Status is JobStatus.InProgress or JobStatus.OnHold or JobStatus.Completed or JobStatus.Cancelled;

    public bool ShowsChecklistRow => Job is not null && !ShowsChecklistLink;

    public int UnsavedAnswers => _jobId == Guid.Empty ? 0 : _inspections.UnsavedCount(_jobId);

    public string ChecklistSubline
    {
        get
        {
            if (Job is not { } job)
            {
                return string.Empty;
            }

            var answered = string.Create(CultureInfo.InvariantCulture, $"{job.Affordances.CanSubmit.Answered} of {job.Affordances.CanSubmit.Total} answered");
            return job.Status switch
            {
                JobStatus.Completed => job.CompletedAt is { } completed ? "Submitted " + DisplayFormats.AtMoment(completed, Zone, Today) : "Submitted",
                JobStatus.Cancelled => answered + NotSubmittedSuffix,
                _ => UnsavedAnswers > 0
                    ? answered + DisplayFormats.Separator + string.Create(CultureInfo.InvariantCulture, $"{UnsavedAnswers} not saved")
                    : answered,
            };
        }
    }

    public string ChecklistLinkDescription => ChecklistName + ", " + ChecklistSubline + ", opens the inspection";

    public bool ShowsInspectionAction => Job?.Status == JobStatus.InProgress;

    public string InspectionActionText => Job?.Affordances.CanSubmit.Answered > 0 ? ContinueInspectionText : StartInspectionText;

    public bool ShowsUpdateStatusPrimary => ShowsUpdateStatus && !ShowsInspectionAction;

    public bool ShowsUpdateStatusSecondary => ShowsUpdateStatus && ShowsInspectionAction;

    public bool CanCall => _phoneDialer.IsSupported && ContactPhone is not null;

    public bool ShowsRowCall => ShowsRowActions && CanCall;

    public string CallLabel => "Call " + (ContactName ?? CustomerName);

    public string NavigateLabel => "Navigate to " + SiteAddress;

    public int NavigateColumnSpan => CanCall ? 1 : 2;

    public bool ShowsPushedHeader => !IsInPane;

    public bool ShowsPaneHero => IsInPane && Job is not null;

    public bool ShowsPaneMeta => !IsAssigned && Job is not null;

    public Thickness ContentPadding => IsInPane ? Thickness.Zero : new Thickness(16, 20, 16, 24);

    public int InfoColumnSpan => IsInPane ? 1 : 2;

    public int DetailsRow => IsInPane ? 0 : 1;

    public int DetailsColumn => IsInPane ? 1 : 0;

    public int DetailsColumnSpan => IsInPane ? 1 : 2;

    private string? ContactName => Blank(Job?.ContactName);

    private string? ContactPhone => Blank(Job?.ContactPhone);

    private TimeZoneInfo Zone => _clock.LocalTimeZone;

    private DateOnly Today => DisplayFormats.LocalDay(_clock.GetUtcNow(), Zone);

    public static string Sentence(JobHistoryEntryResponse entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var actor = entry.Actor.DisplayName;
        return entry.Action switch
        {
            JobHistoryAction.Created => "Created by " + actor,
            JobHistoryAction.Assigned => entry.Technician is null ? "Assigned by " + actor : "Assigned to " + entry.Technician.DisplayName,
            JobHistoryAction.Started => "Started by " + actor,
            JobHistoryAction.PutOnHold => "Put on hold by " + actor,
            JobHistoryAction.Resumed => "Resumed by " + actor,
            JobHistoryAction.Completed => "Completed by " + actor,
            JobHistoryAction.Cancelled => "Cancelled by " + actor,
            _ => "Updated by " + actor,
        };
    }

    public static IReadOnlyList<TimelineEntry> BuildTimeline(JobDetailsResponse job, TimeZoneInfo zone, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(job);

        var history = job.History;
        return [.. history.Select((entry, index) =>
        {
            var day = DisplayFormats.LocalDay(entry.OccurredAt, zone);
            return new TimelineEntry(
                day == today ? null : DisplayFormats.ShortDate(day, today),
                DisplayFormats.Time(entry.OccurredAt, zone),
                Sentence(entry),
                Blank(entry.Note),
                index == history.Count - 1);
        })];
    }

    public static string BuildStatusLine(JobDetailsResponse job, TimeZoneInfo zone, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(job);

        switch (job.Status)
        {
            case JobStatus.InProgress:
            case JobStatus.OnHold:
                var since = job.History.LastOrDefault(entry => entry.ToStatus == job.Status)?.OccurredAt
                    ?? (job.Status == JobStatus.InProgress ? job.StartedAt : null);
                return since is { } moment
                    ? DisplayFormats.StatusLabel(job.Status) + " since " + DisplayFormats.Moment(moment, zone, today)
                    : DisplayFormats.StatusLabel(job.Status);
            case JobStatus.Completed when job.CompletedAt is { } completed:
                return "Completed " + DisplayFormats.AtMoment(completed, zone, today);
            case JobStatus.Cancelled when job.CancelledAt is { } cancelled:
                return "Cancelled " + DisplayFormats.AtMoment(cancelled, zone, today);
            default:
                return string.Empty;
        }
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var notice = query.TryGetValue(Routes.NoticeParameter, out var raw) ? raw as string : null;
        if (notice == Routes.SubmissionInvalidTransitionNotice)
        {
            _pendingNotice = notice;
        }

        var jobId = query.TryGetValue(Routes.JobIdParameter, out var value)
            ? value switch
            {
                Guid id => id,
                string text when Guid.TryParse(text, out var parsed) => parsed,
                _ => Guid.Empty,
            }
            : Guid.Empty;
        if (jobId != Guid.Empty && (jobId != _jobId || _pendingNotice is null))
        {
            _ = ShowJobAsync(jobId);
            return;
        }

        if (_pendingNotice is not null && IsActive && Job is not null)
        {
            _ = RefreshAfterReturnAsync();
        }
    }

    public Task ShowJobAsync(Guid jobId)
    {
        if (jobId != _jobId)
        {
            _jobId = jobId;
            ForgetStatusChanges(keepDraft: false);
            Job = null;
            Timeline = [];
            RefreshError = null;
            _loadedAt = null;
        }

        return LoadJobAsync();
    }

    public void Reset()
    {
        _load?.Cancel();
        _load = null;
        ForgetStatusChanges(keepDraft: false);
        _jobId = Guid.Empty;
        _pendingNotice = null;
        _loadedAt = null;
        Job = null;
        Timeline = [];
        StartsLine = null;
        OfflineMessage = null;
        Error = null;
        RefreshError = null;
        State = ScreenState.Loading;
    }

    public static Uri MapSearchUri(string address) => new(MapSearchAddress + Uri.EscapeDataString(address));

    protected override Task ReloadAsync(CancellationToken cancellationToken) => LoadJobAsync();

    protected override void OnActivated()
    {
        _context = SynchronizationContext.Current;
        _session.SessionEnded += OnSessionEnded;
        _inspections.Changed += OnInspectionsChanged;
        _timer?.Dispose();
        _timer = _clock.CreateTimer(_ => OnMinute(), null, MetaRefreshInterval, MetaRefreshInterval);
        UpdateStartsLine();
        NotifyStatusActions();
        if (Job is not null)
        {
            _ = RefreshAfterReturnAsync();
        }
    }

    protected override void OnDeactivated()
    {
        _session.SessionEnded -= OnSessionEnded;
        _inspections.Changed -= OnInspectionsChanged;
        _timer?.Dispose();
        _timer = null;
    }

    protected override void OnConnectivityChanged(bool isOnline)
    {
        NotifyStatusActions();
        if (!isOnline && State == ScreenState.Content)
        {
            ShowOffline();
        }
        else if (isOnline && State is ScreenState.Offline or ScreenState.Error)
        {
            _ = LoadJobAsync();
        }
    }

    partial void OnJobChanged(JobDetailsResponse? value)
    {
        if (value is not null)
        {
            _inspections.RememberJob(new InspectionJob(value.Id, value.Number, value.Title, value.ChecklistTemplate.Name));
        }

        NotifyStatusActions();
    }

    partial void OnIsInPaneChanged(bool value) => NotifyStatusActions();

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    [RelayCommand]
    private void Appearing() => Activate();

    [RelayCommand]
    private void Disappearing() => Deactivate();

    [RelayCommand]
    private Task LoadAsync() => LoadJobAsync();

    [RelayCommand]
    private async Task BackAsync()
    {
        try
        {
            await _navigator.GoToAsync(Routes.Back);
        }
        catch (Exception exception)
        {
            LogNavigateFailed(_logger, _jobId, exception.GetType().Name);
        }
    }

    [RelayCommand]
    private async Task OpenChecklistAsync()
    {
        if (Job is not { } job || !ShowsChecklistLink)
        {
            return;
        }

        try
        {
            await _navigator.GoToAsync(Routes.InspectionPage, new Dictionary<string, object> { [Routes.JobIdParameter] = job.Id });
        }
        catch (Exception exception)
        {
            LogNavigateFailed(_logger, _jobId, exception.GetType().Name);
        }
    }

    private async Task RefreshAfterReturnAsync()
    {
        var loaded = await LoadJobAsync();
        if (!loaded || _pendingNotice != Routes.SubmissionInvalidTransitionNotice)
        {
            return;
        }

        _pendingNotice = null;
        ActionBanner = new PageBanner(BannerVariant.Conflict, JobNumber + " can no longer be submitted.", "Its status changed after you opened it. The latest version is shown.", null);
        ScrollToTopRequest++;
        TitleFocusRequest++;
    }

    private void OnInspectionsChanged(object? sender, Guid jobId)
    {
        if (jobId != _jobId)
        {
            return;
        }

        if (_context is { } context && !ReferenceEquals(SynchronizationContext.Current, context))
        {
            context.Post(_ => NotifyChecklist(), null);
            return;
        }

        NotifyChecklist();
    }

    private void NotifyChecklist()
    {
        OnPropertyChanged(nameof(UnsavedAnswers));
        OnPropertyChanged(nameof(ChecklistSubline));
        OnPropertyChanged(nameof(ChecklistLinkDescription));
    }

    private bool CanNavigate() => Job is not null;

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private async Task NavigateAsync()
    {
        var address = SiteAddress;
        if (string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        try
        {
            var opened = await _map.TryOpenAsync(new Placemark { Thoroughfare = address }, new MapLaunchOptions());
            if (!opened)
            {
                LogMapFallback(_logger, _jobId);
                await _launcher.OpenAsync(MapSearchUri(address));
            }
        }
        catch (Exception exception)
        {
            LogNavigateFailed(_logger, _jobId, exception.GetType().Name);
        }
    }

    private bool CanCallContact() => CanCall;

    [RelayCommand(CanExecute = nameof(CanCallContact))]
    private void Call()
    {
        if (!CanCall || ContactPhone is not { } phone)
        {
            return;
        }

        try
        {
            _phoneDialer.Open(phone);
        }
        catch (Exception exception)
        {
            LogCallFailed(_logger, _jobId, exception.GetType().Name);
        }
    }

    private async Task<bool> LoadJobAsync()
    {
        _load?.Cancel();
        var load = new CancellationTokenSource();
        _load = load;
        var token = load.Token;
        if (_jobId == Guid.Empty)
        {
            return false;
        }

        if (!IsOnline)
        {
            ShowOffline();
            return false;
        }

        if (Job is null)
        {
            State = ScreenState.Loading;
        }

        try
        {
            var job = await _jobs.GetJobAsync(_jobId, token);
            if (token.IsCancellationRequested)
            {
                return false;
            }

            _loadedAt = _clock.GetUtcNow();
            Job = job;
            Timeline = BuildTimeline(job, Zone, Today);
            Error = null;
            OfflineMessage = null;
            RefreshError = null;
            ActionBanner = null;
            State = ScreenState.Content;
            UpdateStartsLine();
            NotifyChecklist();
            return true;
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            return false;
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            await NoLongerAssignedAsync();
            return false;
        }
        catch (Exception exception) when (IsFailure(exception, token))
        {
            if (Job is not null && _loadedAt is { } loadedAt)
            {
                Error = null;
                OfflineMessage = null;
                ActionBanner = null;
                RefreshError = DescribeRefresh(exception, RefreshFailedTitle, ShowingThisJobAsOf(loadedAt));
                State = ScreenState.Content;
                return false;
            }

            RefreshError = null;
            Error = Describe(exception, LoadFailedTitle);
            State = ScreenState.Error;
            return false;
        }
    }

    private void ShowOffline()
    {
        RefreshError = null;
        ActionBanner = null;
        if (Job is not null && _loadedAt is { } loadedAt)
        {
            OfflineMessage = ShowingThisJobAsOf(loadedAt);
            Error = null;
            State = ScreenState.Offline;
            return;
        }

        Error = new ScreenError(LoadFailedTitle, CheckConnectionMessage, null);
        State = ScreenState.Error;
    }

    private string ShowingThisJobAsOf(DateTimeOffset loadedAt) => "Showing this job as of " + DisplayFormats.Time(loadedAt, Zone) + ".";

    private async Task NoLongerAssignedAsync()
    {
        LogNoLongerAssigned(_logger, _jobId);
        if (IsInPane)
        {
            JobUnavailable?.Invoke(this, EventArgs.Empty);
            return;
        }

        try
        {
            await _navigator.GoToAsync(Routes.Back, new Dictionary<string, object> { [Routes.NoticeParameter] = Routes.JobUnassignedNotice });
        }
        catch (Exception exception)
        {
            LogNavigateFailed(_logger, _jobId, exception.GetType().Name);
        }
    }

    private void OnSessionEnded(object? sender, EventArgs e)
    {
        if (_context is { } context && !ReferenceEquals(SynchronizationContext.Current, context))
        {
            context.Post(_ => Reset(), null);
            return;
        }

        Reset();
    }

    private void OnMinute()
    {
        if (_context is { } context && !ReferenceEquals(SynchronizationContext.Current, context))
        {
            context.Post(_ => UpdateStartsLine(), null);
            return;
        }

        UpdateStartsLine();
    }

    private void UpdateStartsLine() =>
        StartsLine = Job is { Status: JobStatus.Assigned } job
            ? DisplayFormats.StartsLine(job.ScheduledStart, _clock.GetUtcNow(), Zone)
            : null;

    [LoggerMessage(EventName = "jobs.no-longer-assigned", Level = LogLevel.Information, Message = "jobs.no-longer-assigned {JobId}")]
    private static partial void LogNoLongerAssigned(ILogger logger, Guid jobId);

    [LoggerMessage(EventName = "jobs.map-fallback", Level = LogLevel.Information, Message = "jobs.map-fallback {JobId}")]
    private static partial void LogMapFallback(ILogger logger, Guid jobId);

    [LoggerMessage(EventName = "jobs.navigate-failed", Level = LogLevel.Warning, Message = "jobs.navigate-failed {JobId} {Failure}")]
    private static partial void LogNavigateFailed(ILogger logger, Guid jobId, string failure);

    [LoggerMessage(EventName = "jobs.call-failed", Level = LogLevel.Warning, Message = "jobs.call-failed {JobId} {Failure}")]
    private static partial void LogCallFailed(ILogger logger, Guid jobId, string failure);
}
