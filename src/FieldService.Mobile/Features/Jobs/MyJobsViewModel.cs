using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FieldService.Mobile.Shared.Connectivity;
using FieldService.Mobile.Shared.Formatting;
using FieldService.Mobile.Shared.Navigation;
using FieldService.Mobile.Shared.Screens;
using FieldService.Mobile.Shared.Session;
using Microsoft.Extensions.Logging;

namespace FieldService.Mobile.Features.Jobs;

public sealed partial class MyJobsViewModel : ScreenViewModel, IQueryAttributable
{
    public const string PageTitle = "My jobs";
    public const string LoadFailedTitle = "Jobs could not be loaded.";
    public const string RefreshFailedTitle = "Jobs could not be refreshed.";
    public const string OfflineTitle = "You're offline.";
    public const string LoadingText = "Loading your jobs";
    public const string NoLongerAssignedText = "This job is no longer assigned to you.";
    public const string SubmittedMessage = "It is now under Completed.";

    public static readonly TimeSpan SuccessDuration = TimeSpan.FromSeconds(8);

    private readonly JobsApiClient _jobs;
    private readonly ISemanticScreenReader _screenReader;
    private ITimer? _successTimer;
    private TimeSpan _successRemaining;
    private DateTimeOffset _successShownAt;
    private readonly ISessionService _session;
    private readonly INavigator _navigator;
    private readonly AppStartup _startup;
    private readonly IWindowLayout _layout;
    private readonly Func<JobDetailsViewModel> _createDetails;
    private readonly TimeProvider _clock;
    private readonly ILogger<MyJobsViewModel> _logger;
    private readonly SynchronizationContext? _context;
    private readonly Dictionary<AssignedJobsView, LoadedJobs> _loaded = [];
    private CancellationTokenSource? _load;
    private JobDetailsViewModel? _details;
    private Guid? _loadedFor;

    public MyJobsViewModel(
        JobsApiClient jobs,
        ISessionService session,
        INavigator navigator,
        AppStartup startup,
        IWindowLayout layout,
        Func<JobDetailsViewModel> createDetails,
        ISemanticScreenReader screenReader,
        NetworkStatus network,
        TimeProvider clock,
        ILogger<MyJobsViewModel> logger)
        : base(network)
    {
        _jobs = jobs;
        _screenReader = screenReader;
        _session = session;
        _navigator = navigator;
        _startup = startup;
        _layout = layout;
        _createDetails = createDetails;
        _clock = clock;
        _logger = logger;
        _context = SynchronizationContext.Current;
        Groups = [];
        SelectedView = AssignedJobsView.Active;
        IsTwoPane = layout.IsTwoPane;
        session.SessionEnded += OnSessionEnded;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActiveSelected), nameof(IsCompletedSelected), nameof(EmptyTitle), nameof(EmptyMessage), nameof(ActiveSegmentDescription), nameof(CompletedSegmentDescription))]
    public partial AssignedJobsView SelectedView { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<JobDayGroup> Groups { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveCount), nameof(ActiveCountText), nameof(ActiveSegmentDescription))]
    public partial int? ActiveCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncText), nameof(SyncIndicator), nameof(UpdatedText))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncText), nameof(SyncIndicator), nameof(UpdatedText))]
    public partial DateTimeOffset? LastUpdated { get; private set; }

    [ObservableProperty]
    public partial string? OfflineMessage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string? Notice { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuccess))]
    public partial string? SuccessTitle { get; private set; }

    public bool HasSuccess => SuccessTitle is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSinglePane))]
    public partial bool IsTwoPane { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(ShowsNoSelection))]
    public partial JobListItem? SelectedJob { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOpenSheet))]
    public partial bool IsAccountSheetOpen { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignOutCommand))]
    public partial bool IsSigningOut { get; private set; }

    public JobDetailsViewModel Details => _details ??= CreatePaneDetails();

    public bool ShowsSinglePane => !IsTwoPane;

    public bool HasSelection => SelectedJob is not null;

    public bool ShowsNoSelection => SelectedJob is null;

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    public bool HasOpenSheet => IsAccountSheetOpen || _details?.IsStatusSheetOpen == true;

    public bool IsActiveSelected => SelectedView == AssignedJobsView.Active;

    public bool IsCompletedSelected => SelectedView == AssignedJobsView.Completed;

    public bool HasActiveCount => ActiveCount is not null;

    public string ActiveCountText => ActiveCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    public string ActiveSegmentDescription => Segment("Active", ActiveCountText, IsActiveSelected);

    public string CompletedSegmentDescription => Segment("Completed", string.Empty, IsCompletedSelected);

    public string EmptyTitle => IsCompletedSelected ? "No completed jobs" : "No jobs assigned";

    public string EmptyMessage => IsCompletedSelected
        ? "Jobs you submit appear here, along with any job dispatch cancels."
        : "New assignments appear here as soon as dispatch sends them.";

    public string Eyebrow => DisplayFormats.HeaderDate(Today);

    public string DisplayName => _session.User?.DisplayName ?? string.Empty;

    public string? Email => _session.User?.Email;

    public bool HasEmail => !string.IsNullOrEmpty(Email);

    public string Initials => DisplayFormats.Initials(_session.User?.DisplayName);

    public string UpdatedText => LastUpdated is { } updated ? "Updated " + DisplayFormats.Time(updated, Zone) : string.Empty;

    public SyncIndicator SyncIndicator => IsBusy
        ? SyncIndicator.Loading
        : State == ScreenState.Offline ? SyncIndicator.Offline : SyncIndicator.Updated;

    public string SyncText => SyncIndicator switch
    {
        SyncIndicator.Loading => LoadingText,
        SyncIndicator.Offline when LastUpdated is { } updated => "Last updated " + DisplayFormats.Time(updated, Zone),
        _ when LastUpdated is not null => UpdatedText + DisplayFormats.Separator + "pull down to refresh",
        _ => string.Empty,
    };

    private TimeZoneInfo Zone => _clock.LocalTimeZone;

    private DateOnly Today => DisplayFormats.LocalDay(_clock.GetUtcNow(), Zone);

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var notice = query.TryGetValue(Routes.NoticeParameter, out var raw) ? raw as string : null;
        if (notice == Routes.JobUnassignedNotice)
        {
            Notice = NoLongerAssignedText;
        }
        else if (notice == Routes.JobSubmittedNotice && query.TryGetValue(Routes.JobNumberParameter, out var number) && number is string jobNumber)
        {
            ShowSubmitted(jobNumber);
        }
        else if (notice == Routes.SubmissionInvalidTransitionNotice && IsTwoPane)
        {
            Details.ApplyQueryAttributes(query);
        }
    }

    public bool CloseSheet()
    {
        if (_details?.CloseSheet() == true)
        {
            return true;
        }

        if (!IsAccountSheetOpen)
        {
            return false;
        }

        IsAccountSheetOpen = false;
        return true;
    }

    protected override Task ReloadAsync(CancellationToken cancellationToken) => LoadCurrentAsync();

    protected override void OnActivated()
    {
        _layout.Changed += OnLayoutChanged;
        IsTwoPane = _layout.IsTwoPane;
        if (IsTwoPane)
        {
            Details.Activate();
        }

        OnPropertyChanged(nameof(Eyebrow));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Email));
        OnPropertyChanged(nameof(HasEmail));
        OnPropertyChanged(nameof(Initials));
        StartSuccessTimer();
    }

    protected override void OnDeactivated()
    {
        StopSuccessTimer();
        _layout.Changed -= OnLayoutChanged;
        _details?.Deactivate();
        IsAccountSheetOpen = false;
    }

    protected override void OnConnectivityChanged(bool isOnline)
    {
        if (!isOnline && State is ScreenState.Content or ScreenState.Empty)
        {
            ShowOffline(SelectedView);
        }
        else if (isOnline && State is ScreenState.Offline or ScreenState.Error)
        {
            _ = LoadCurrentAsync();
        }
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(State))
        {
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(SyncIndicator)));
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(SyncText)));
        }
    }

    private void ShowSubmitted(string jobNumber)
    {
        StopSuccessTimer();
        Notice = null;
        SuccessTitle = jobNumber + " submitted.";
        _successRemaining = SuccessDuration;
        _screenReader.Announce(SuccessTitle);
        if (SelectedView != AssignedJobsView.Active)
        {
            _ = SelectViewAsync(AssignedJobsView.Active);
        }

        if (IsActive)
        {
            StartSuccessTimer();
        }
    }

    private void StartSuccessTimer()
    {
        if (SuccessTitle is null || _successTimer is not null)
        {
            return;
        }

        _successShownAt = _clock.GetUtcNow();
        _successTimer = _clock.CreateTimer(_ => OnSuccessElapsed(), null, _successRemaining, Timeout.InfiniteTimeSpan);
    }

    private void StopSuccessTimer()
    {
        if (_successTimer is null)
        {
            return;
        }

        _successTimer.Dispose();
        _successTimer = null;
        _successRemaining -= _clock.GetUtcNow() - _successShownAt;
        if (_successRemaining < TimeSpan.Zero)
        {
            _successRemaining = TimeSpan.Zero;
        }
    }

    private void OnSuccessElapsed()
    {
        void Clear()
        {
            _successTimer?.Dispose();
            _successTimer = null;
            SuccessTitle = null;
        }

        if (_context is { } context && !ReferenceEquals(SynchronizationContext.Current, context))
        {
            context.Post(_ => Clear(), null);
            return;
        }

        Clear();
    }

    private static string Segment(string label, string count, bool selected)
    {
        var name = string.IsNullOrEmpty(count) ? label : label + ", " + count;
        return name + (selected ? ", selected" : ", not selected");
    }

    [RelayCommand]
    private Task AppearingAsync()
    {
        if (_loadedFor is { } owner && _session.User?.Id != owner)
        {
            ForgetUser();
        }

        Activate();
        return LoadCurrentAsync();
    }

    [RelayCommand]
    private void Disappearing() => Deactivate();

    [RelayCommand]
    private void Loaded() => _startup.ReportShown(Routes.Jobs);

    [RelayCommand]
    private Task RefreshAsync()
    {
        Notice = null;
        return LoadCurrentAsync();
    }

    [RelayCommand]
    private Task SelectViewAsync(AssignedJobsView view)
    {
        if (view == SelectedView && State != ScreenState.Error)
        {
            return Task.CompletedTask;
        }

        SelectedView = view;
        Notice = null;
        SelectedJob = null;
        RefreshError = null;
        if (_loaded.TryGetValue(view, out var loaded))
        {
            Show(view, loaded);
        }
        else
        {
            Groups = [];
            LastUpdated = null;
            State = ScreenState.Loading;
        }

        return LoadCurrentAsync();
    }

    [RelayCommand]
    private async Task OpenJobAsync(JobListItem? job)
    {
        if (job is null)
        {
            return;
        }

        if (IsTwoPane)
        {
            SelectedJob = job;
            await Details.ShowJobAsync(job.Id);
            return;
        }

        SelectedJob = null;
        try
        {
            await _navigator.GoToAsync(Routes.JobDetails, new Dictionary<string, object> { [Routes.JobIdParameter] = job.Id });
        }
        catch (Exception exception)
        {
            LogOpenFailed(_logger, job.Id, exception.GetType().Name);
        }
    }

    [RelayCommand]
    private void OpenAccountSheet() => IsAccountSheetOpen = true;

    [RelayCommand]
    private void CloseAccountSheet() => IsAccountSheetOpen = false;

    private bool CanSignOut() => !IsSigningOut;

    [RelayCommand(CanExecute = nameof(CanSignOut))]
    private async Task SignOutAsync(CancellationToken cancellationToken)
    {
        IsSigningOut = true;
        try
        {
            await _session.SignOutAsync(cancellationToken);
            ForgetUser();
        }
        finally
        {
            IsSigningOut = false;
        }
    }

    private async Task LoadCurrentAsync()
    {
        _load?.Cancel();
        var load = new CancellationTokenSource();
        _load = load;
        var view = SelectedView;
        var token = load.Token;

        if (!IsOnline)
        {
            ShowOffline(view);
            IsRefreshing = false;
            return;
        }

        if (!_loaded.ContainsKey(view))
        {
            Groups = [];
            LastUpdated = null;
            State = ScreenState.Loading;
        }

        var requestedFor = _session.User?.Id;
        IsBusy = true;
        try
        {
            var response = await _jobs.GetAssignedJobsAsync(view, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            var answeredFor = _session.User?.Id;
            if (requestedFor is { } requester && answeredFor != requester)
            {
                LogUserChanged(_logger, JobsApiClient.ViewValue(view));
                ForgetUser();
                if (answeredFor is not null && IsActive)
                {
                    _ = LoadCurrentAsync();
                }

                return;
            }

            var loaded = new LoadedJobs(response.Items, _clock.GetUtcNow());
            _loaded[view] = loaded;
            _loadedFor = answeredFor;
            if (view == AssignedJobsView.Active)
            {
                ActiveCount = response.Items.Count;
            }

            Error = null;
            OfflineMessage = null;
            RefreshError = null;
            Show(view, loaded);
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsFailure(exception, token))
        {
            LogLoadFailed(_logger, JobsApiClient.ViewValue(view), exception.GetType().Name);
            if (_loaded.TryGetValue(view, out var shown))
            {
                ShowRefreshFailed(view, shown, exception);
            }
            else
            {
                Groups = [];
                RefreshError = null;
                LastUpdated = null;
                Error = Describe(exception, LoadFailedTitle);
                State = ScreenState.Error;
            }
        }
        finally
        {
            if (ReferenceEquals(_load, load))
            {
                IsBusy = false;
                IsRefreshing = false;
            }
        }
    }

    private void Show(AssignedJobsView view, LoadedJobs loaded)
    {
        Groups = JobDayGroup.Build(loaded.Items, view, Zone, Today);
        LastUpdated = loaded.LoadedAt;
        State = loaded.Items.Count == 0 ? ScreenState.Empty : ScreenState.Content;
        KeepSelection();
    }

    private void ShowRefreshFailed(AssignedJobsView view, LoadedJobs loaded, Exception exception)
    {
        Error = null;
        OfflineMessage = null;
        RefreshError = DescribeRefresh(exception, RefreshFailedTitle, ShowingJobsFrom(loaded.LoadedAt));
        Show(view, loaded);
    }

    private void ShowOffline(AssignedJobsView view)
    {
        RefreshError = null;
        if (_loaded.TryGetValue(view, out var loaded))
        {
            Groups = JobDayGroup.Build(loaded.Items, view, Zone, Today);
            LastUpdated = loaded.LoadedAt;
            OfflineMessage = ShowingJobsFrom(loaded.LoadedAt);
            Error = null;
            State = ScreenState.Offline;
            return;
        }

        Groups = [];
        LastUpdated = null;
        Error = new ScreenError(LoadFailedTitle, CheckConnectionMessage, null);
        State = ScreenState.Error;
    }

    private string ShowingJobsFrom(DateTimeOffset loadedAt) => "Showing jobs from " + DisplayFormats.Time(loadedAt, Zone) + ".";

    private void KeepSelection()
    {
        if (SelectedJob is { } selected)
        {
            SelectedJob = Groups.SelectMany(group => group).FirstOrDefault(job => job.Id == selected.Id);
        }
    }

    private JobDetailsViewModel CreatePaneDetails()
    {
        var details = _createDetails();
        details.IsInPane = true;
        details.JobUnavailable += OnPaneJobUnavailable;
        details.StatusChanged += OnPaneStatusChanged;
        details.PropertyChanged += OnPanePropertyChanged;
        return details;
    }

    private void OnPaneStatusChanged(object? sender, EventArgs e) => _ = LoadCurrentAsync();

    private void OnPanePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(JobDetailsViewModel.IsStatusSheetOpen))
        {
            OnPropertyChanged(nameof(HasOpenSheet));
        }
    }

    private void OnPaneJobUnavailable(object? sender, EventArgs e)
    {
        SelectedJob = null;
        Notice = NoLongerAssignedText;
        _ = LoadCurrentAsync();
    }

    private void OnSessionEnded(object? sender, EventArgs e)
    {
        if (_context is { } context && !ReferenceEquals(SynchronizationContext.Current, context))
        {
            context.Post(_ => ForgetUser(), null);
            return;
        }

        ForgetUser();
    }

    private void ForgetUser()
    {
        _load?.Cancel();
        _load = null;
        _loaded.Clear();
        _loadedFor = null;
        _details?.Reset();
        SelectedJob = null;
        Groups = [];
        ActiveCount = null;
        LastUpdated = null;
        OfflineMessage = null;
        Notice = null;
        _successTimer?.Dispose();
        _successTimer = null;
        SuccessTitle = null;
        Error = null;
        RefreshError = null;
        IsBusy = false;
        IsRefreshing = false;
        IsAccountSheetOpen = false;
        SelectedView = AssignedJobsView.Active;
        State = ScreenState.Loading;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Email));
        OnPropertyChanged(nameof(HasEmail));
        OnPropertyChanged(nameof(Initials));
    }

    private void OnLayoutChanged(object? sender, EventArgs e)
    {
        IsTwoPane = _layout.IsTwoPane;
        if (IsTwoPane)
        {
            Details.Activate();
            return;
        }

        SelectedJob = null;
        _details?.Deactivate();
    }

    [LoggerMessage(EventName = "jobs.load-failed", Level = LogLevel.Warning, Message = "jobs.load-failed {View} {Failure}")]
    private static partial void LogLoadFailed(ILogger logger, string view, string failure);

    [LoggerMessage(EventName = "jobs.user-changed", Level = LogLevel.Information, Message = "jobs.user-changed {View}")]
    private static partial void LogUserChanged(ILogger logger, string view);

    [LoggerMessage(EventName = "jobs.open-failed", Level = LogLevel.Warning, Message = "jobs.open-failed {JobId} {Failure}")]
    private static partial void LogOpenFailed(ILogger logger, Guid jobId, string failure);

    private sealed record LoadedJobs(IReadOnlyList<AssignedJobResponse> Items, DateTimeOffset LoadedAt);
}
