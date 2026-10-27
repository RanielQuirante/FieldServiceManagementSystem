using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Connectivity;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Shared.Formatting;
using FieldService.Mobile.Shared.Http;
using FieldService.Mobile.Shared.Navigation;
using FieldService.Mobile.Shared.Screens;
using FieldService.Mobile.Shared.Session;
using Microsoft.Extensions.Logging;

namespace FieldService.Mobile.Features.Inspection;

public sealed record FailedItem(string NumberText, string Prompt, string? Note, IReadOnlyList<string> Readings)
{
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    public string Description => string.Join(", ", new[] { "Item " + NumberText, "Fail", Prompt, Note }.Concat(Readings).Where(part => !string.IsNullOrWhiteSpace(part)));

    public override string ToString() => $"{nameof(FailedItem)} {NumberText}";
}

public sealed partial class ReviewAndSubmitViewModel : ScreenViewModel, IQueryAttributable
{
    public const string PageTitle = "Review and submit";
    public const string BackToInspection = "Back to inspection";
    public const string BackToJobDetails = "Back to job details";
    public const string LoadingText = "Loading the review";
    public const string SummaryTitle = "Summary";
    public const string TimeOnSiteLabel = "Time on site";
    public const string ChecklistLabel = "Checklist";
    public const string NotesLabel = "Technician notes";
    public const string NotesHelp = "Dispatch sees these notes in the inspection report.";
    public const int NotesMaxLength = 2000;
    public const int NotesCountFrom = 1800;
    public const string ConfirmationText = "I confirm the site is safe and the customer has been informed";
    public const string ConfirmationRequired = "Confirm the site is safe before you submit.";
    public const string SubmitText = "Submit completed job";
    public const string SubmittingText = "Submitting…";
    public const string SubmitHelper = "After you submit, the job moves to Completed and dispatch is notified.";
    public const string OfflineHelper = "You're offline. Connect to submit the job.";
    public const string OfflineTitle = "You're offline.";
    public const string OfflineMessage = "Your notes stay on this device until you're back online.";
    public const string SubmitFailedTitle = "The job could not be submitted.";
    public const string TryAgainSoon = "Try again in a moment.";
    public const string ConflictMessage = "The latest version is shown. Check it, then try again.";
    public const string LessThanAMinute = "Less than a minute";
    public const string SiteSafetyKey = "siteSafetyConfirmed";
    public const string TechnicianNotesKey = "technicianNotes";

    public static readonly TimeSpan MinuteInterval = TimeSpan.FromMinutes(1);

    private readonly InspectionApiClient _api;
    private readonly JobsApiClient _jobs;
    private readonly InspectionStore _store;
    private readonly ISessionService _session;
    private readonly INavigator _navigator;
    private readonly IWindowLayout _layout;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReviewAndSubmitViewModel> _logger;
    private CancellationTokenSource? _load;
    private ITimer? _timer;
    private SynchronizationContext? _context;
    private InspectionResponse? _inspection;
    private Guid _jobId;
    private string _source = Routes.FromSheet;
    private bool _applyingNotes;

    public ReviewAndSubmitViewModel(
        InspectionApiClient api,
        JobsApiClient jobs,
        InspectionStore store,
        ISessionService session,
        INavigator navigator,
        IWindowLayout layout,
        NetworkStatus network,
        TimeProvider clock,
        ILogger<ReviewAndSubmitViewModel> logger)
        : base(network)
    {
        _api = api;
        _jobs = jobs;
        _store = store;
        _session = session;
        _navigator = navigator;
        _layout = layout;
        _clock = clock;
        _logger = logger;
        FailedItems = [];
    }

    [ObservableProperty]
    public partial string JobNumber { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string JobTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string TimeOnSiteText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string PassText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string FailText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string NotApplicableText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial int PassCount { get; private set; }

    [ObservableProperty]
    public partial int FailCount { get; private set; }

    [ObservableProperty]
    public partial int NotApplicableCount { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<FailedItem> FailedItems { get; private set; }

    [ObservableProperty]
    public partial string TechnicianNotes { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? NotesError { get; private set; }

    [ObservableProperty]
    public partial int NotesFocusRequest { get; private set; }

    [ObservableProperty]
    public partial int TitleFocusRequest { get; private set; }

    [ObservableProperty]
    public partial bool IsConfirmed { get; set; }

    [ObservableProperty]
    public partial string? ConfirmationError { get; private set; }

    [ObservableProperty]
    public partial int ConfirmationFocusRequest { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubmitLabel), nameof(IsInputBlocked))]
    public partial bool IsSubmitting { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSlotBanner))]
    public partial PageBanner? SlotBanner { get; private set; }

    [ObservableProperty]
    public partial int ScrollToTopRequest { get; private set; }

    [ObservableProperty]
    public partial int SubmitFocusRequest { get; private set; }

    [ObservableProperty]
    public partial int FocusInViewRequest { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsDock), nameof(ShowsBarSpacer))]
    public partial bool IsKeyboardOpen { get; set; }

    public Guid JobId => _jobId;

    public string Source => _source;

    public InspectionResponse? Inspection => _inspection;

    public double ColumnMaxWidth => DesktopColumn.MaxWidth(_layout);

    public LayoutOptions ColumnOptions => DesktopColumn.Options(_layout);

    public string BackDescription => _source == Routes.FromInspection ? BackToInspection : BackToJobDetails;

    public bool HasSlotBanner => SlotBanner is not null;

    public bool HasFailedItems => FailedItems.Count > 0;

    public bool ShowsDock => State == ScreenState.Content && !IsKeyboardOpen;

    public bool ShowsBarSpacer => !IsKeyboardOpen;

    public bool ShowsOfflineBanner => !IsOnline && State == ScreenState.Content;

    public bool IsSubmitOffline => !IsOnline;

    public string HelperText => IsOnline ? SubmitHelper : OfflineHelper;

    public string SubmitLabel => IsSubmitting ? SubmittingText : SubmitText;

    public bool IsInputBlocked => IsSubmitting;

    private TimeZoneInfo Zone => _clock.LocalTimeZone;

    public static string TimeOnSite(DateTimeOffset start, DateTimeOffset now, TimeZoneInfo zone, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var elapsed = ToMinute(now) - ToMinute(start);
        var duration = elapsed < TimeSpan.FromMinutes(1) ? LessThanAMinute : DisplayFormats.Duration(elapsed);
        var startDay = DisplayFormats.LocalDay(start, zone);
        var range = startDay == DisplayFormats.LocalDay(now, zone)
            ? DisplayFormats.BreakAfterEnDash(DisplayFormats.TimeWindow(start, now, zone, today))
            : DisplayFormats.ShortDate(startDay, today) + ", " + DisplayFormats.Time(start, zone) + DisplayFormats.NoBreakSpace + DisplayFormats.EnDash + " " + DisplayFormats.Time(now, zone);
        return range + DisplayFormats.Separator + duration;
    }

    public static DateTimeOffset ToMinute(DateTimeOffset moment) =>
        new(moment.Ticks - (moment.Ticks % TimeSpan.TicksPerMinute), moment.Offset);

    public static IReadOnlyList<FailedItem> FailedItemsOf(InspectionResponse inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        var total = inspection.Items.Count;
        return [.. inspection.Items
            .OrderBy(item => item.Position)
            .Where(item => item.Answer?.Result == InspectionResult.Fail)
            .Select(item => new FailedItem(
                InspectionItemViewModel.Number(item.Position, total) + " / " + InspectionItemViewModel.Number(total, total),
                item.Prompt,
                string.IsNullOrWhiteSpace(item.Answer!.Note) ? null : item.Answer.Note.Trim(),
                [.. item.ReadingFields
                    .OrderBy(field => field.Position)
                    .Select(field => (field, value: item.Answer.Readings.FirstOrDefault(reading => reading.ReadingFieldId == field.ReadingFieldId)))
                    .Where(pair => pair.value is not null)
                    .Select(pair => pair.field.Label + " " + ReadingValue.WithUnit(pair.value!.Value, pair.field.Unit))]))];
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.TryGetValue(Routes.SourceParameter, out var source) && source is string text)
        {
            _source = text == Routes.FromInspection ? Routes.FromInspection : Routes.FromSheet;
            OnPropertyChanged(nameof(BackDescription));
            OnPropertyChanged(nameof(Source));
        }

        if (query.TryGetValue(Routes.JobIdParameter, out var value))
        {
            var jobId = value switch
            {
                Guid id => id,
                string raw when Guid.TryParse(raw, out var parsed) => parsed,
                _ => Guid.Empty,
            };
            if (jobId != Guid.Empty)
            {
                Open(jobId);
            }
        }
    }

    public bool HandleBackButton()
    {
        if (!IsSubmitting)
        {
            _ = BackCommand.ExecuteAsync(null);
        }

        return true;
    }

    protected override Task ReloadAsync(CancellationToken cancellationToken) => LoadAsync();

    protected override void RequestContentFocus() => TitleFocusRequest++;

    protected override void OnActivated()
    {
        _context = SynchronizationContext.Current;
        _session.SessionEnded += OnSessionEnded;
        _layout.Changed += OnLayoutChanged;
        NotifyColumn();
        _timer?.Dispose();
        _timer = _clock.CreateTimer(_ => OnMinute(), null, MinuteInterval, MinuteInterval);
        UpdateTimeOnSite();
    }

    protected override void OnDeactivated()
    {
        _session.SessionEnded -= OnSessionEnded;
        _layout.Changed -= OnLayoutChanged;
        _timer?.Dispose();
        _timer = null;
    }

    protected override void OnConnectivityChanged(bool isOnline)
    {
        NotifyScreen();
        if (isOnline && State == ScreenState.Error)
        {
            _ = LoadAsync();
        }
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(State))
        {
            NotifyScreen();
        }
    }

    partial void OnTechnicianNotesChanged(string value)
    {
        if (_applyingNotes)
        {
            return;
        }

        NotesError = null;
        if (_jobId != Guid.Empty)
        {
            _store.SetNotes(_jobId, value);
        }
    }

    partial void OnIsConfirmedChanged(bool value)
    {
        if (value)
        {
            ConfirmationError = null;
        }
    }

    [RelayCommand]
    private void Appearing() => Activate();

    [RelayCommand]
    private void Disappearing() => Deactivate();

    [RelayCommand]
    private async Task BackAsync()
    {
        if (IsSubmitting)
        {
            return;
        }

        await GoAsync(Routes.Back, null);
    }

    private bool CanSubmit() => IsOnline && State == ScreenState.Content;

    [RelayCommand(CanExecute = nameof(CanSubmit), AllowConcurrentExecutions = true)]
    private async Task SubmitAsync()
    {
        if (IsSubmitting || _inspection is not { } inspection)
        {
            return;
        }

        if (!IsConfirmed)
        {
            ConfirmationError = ConfirmationRequired;
            ConfirmationFocusRequest++;
            return;
        }

        var notes = string.IsNullOrWhiteSpace(TechnicianNotes) ? null : TechnicianNotes.Trim();
        var request = new SubmitJobRequest(notes, true, inspection.Version);
        var requestedFor = _session.User?.Id;
        SlotBanner = null;
        IsSubmitting = true;
        try
        {
            var job = await _api.SubmitJobAsync(_jobId, request, CancellationToken.None);
            if (Dropped(requestedFor))
            {
                return;
            }

            LogSubmitted(_logger, _jobId);
            await EndAsSubmissionAsync(job);
        }
        catch (Exception) when (Dropped(requestedFor))
        {
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.BadRequest && exception.Problem?.FirstError(SiteSafetyKey) is { } message)
        {
            LogSubmitFailed(_logger, _jobId, 400);
            IsConfirmed = false;
            ConfirmationError = message;
            ConfirmationFocusRequest++;
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.BadRequest && exception.Problem?.FirstError(TechnicianNotesKey) is { } message)
        {
            LogSubmitFailed(_logger, _jobId, 400);
            NotesError = message;
            NotesFocusRequest++;
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            LogSubmitFailed(_logger, _jobId, 404);
            await GoAsync(Routes.Jobs, new Dictionary<string, object> { [Routes.NoticeParameter] = Routes.JobUnassignedNotice });
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict && exception.Code == SubmitAffordance.IncompleteCode)
        {
            LogSubmitFailed(_logger, _jobId, 409);
            var parameters = new Dictionary<string, object>
            {
                [Routes.JobIdParameter] = _jobId,
                [Routes.NoticeParameter] = Routes.SubmissionIncompleteNotice,
            };
            await GoAsync(_source == Routes.FromInspection ? Routes.Back : Routes.BackToInspection, parameters);
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict && exception.Code == SubmitAffordance.InvalidTransitionCode)
        {
            LogSubmitFailed(_logger, _jobId, 409);
            await ReturnAsInvalidTransitionAsync();
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict && exception.Code == InspectionViewModel.LockedCode)
        {
            LogSubmitFailed(_logger, _jobId, 409);
            await ResolveLockedSubmissionAsync(requestedFor);
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            LogSubmitFailed(_logger, _jobId, 409);
            await LoadAsync();
            if (State == ScreenState.Content && RefreshError is null)
            {
                SlotBanner = new PageBanner(BannerVariant.Conflict, JobNumber + " changed after you opened it.", ConflictMessage, null);
                ScrollToTopRequest++;
                SubmitFocusRequest++;
            }
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException or OperationCanceledException)
        {
            LogSubmitFailed(_logger, _jobId, exception is ApiException api ? (int)api.StatusCode : 0);
            ShowSubmitFailure(exception);
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    private async Task ResolveLockedSubmissionAsync(Guid? requestedFor)
    {
        JobDetailsResponse job;
        try
        {
            job = await _jobs.GetJobAsync(_jobId, CancellationToken.None);
        }
        catch (Exception) when (Dropped(requestedFor))
        {
            return;
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            await GoAsync(Routes.Jobs, new Dictionary<string, object> { [Routes.NoticeParameter] = Routes.JobUnassignedNotice });
            return;
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException or OperationCanceledException)
        {
            LogSubmitFailed(_logger, _jobId, exception is ApiException api ? (int)api.StatusCode : 0);
            ShowSubmitFailure(exception);
            return;
        }

        if (Dropped(requestedFor))
        {
            return;
        }

        if (job.Status == JobStatus.Completed)
        {
            LogSubmitted(_logger, _jobId);
            await EndAsSubmissionAsync(job);
            return;
        }

        await ReturnAsInvalidTransitionAsync();
    }

    private async Task EndAsSubmissionAsync(JobDetailsResponse job)
    {
        _store.ClearDraft(_jobId);
        await GoAsync(Routes.Jobs, new Dictionary<string, object>
        {
            [Routes.NoticeParameter] = Routes.JobSubmittedNotice,
            [Routes.JobNumberParameter] = DisplayFormats.JobNumber(job.Number),
        });
    }

    private async Task ReturnAsInvalidTransitionAsync()
    {
        var parameters = new Dictionary<string, object>
        {
            [Routes.JobIdParameter] = _jobId,
            [Routes.NoticeParameter] = Routes.SubmissionInvalidTransitionNotice,
        };
        await GoAsync(_source == Routes.FromInspection ? Routes.BackTwo : Routes.Back, parameters);
    }

    private void ShowSubmitFailure(Exception exception)
    {
        SlotBanner = exception switch
        {
            ApiException { StatusCode: HttpStatusCode.TooManyRequests } limited => new PageBanner(BannerVariant.Error, SubmitFailedTitle, RetryWait(limited.RetryAfter), null),
            ApiException failed => new PageBanner(BannerVariant.Error, SubmitFailedTitle, TryAgainSoon, failed.CorrelationId),
            _ => new PageBanner(BannerVariant.Error, SubmitFailedTitle, CheckConnectionMessage, null),
        };
        ScrollToTopRequest++;
    }

    private void Open(Guid jobId)
    {
        _load?.Cancel();
        _jobId = jobId;
        IsConfirmed = false;
        ConfirmationError = null;
        NotesError = null;
        SlotBanner = null;
        var job = _store.JobFor(jobId);
        JobNumber = job is null ? string.Empty : DisplayFormats.JobNumber(job.Number);
        JobTitle = job?.Title ?? string.Empty;
        _applyingNotes = true;
        try
        {
            TechnicianNotes = _store.Notes(jobId);
        }
        finally
        {
            _applyingNotes = false;
        }

        if (_source == Routes.FromInspection && _store.Loaded(jobId) is { } loaded)
        {
            Show(loaded.Inspection);
            State = ScreenState.Content;
            return;
        }

        _inspection = null;
        State = ScreenState.Loading;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _load?.Cancel();
        var load = new CancellationTokenSource();
        _load = load;
        var token = load.Token;
        if (_jobId == Guid.Empty)
        {
            return;
        }

        if (!IsOnline)
        {
            if (_inspection is null)
            {
                Error = new ScreenError(InspectionViewModel.LoadFailedTitle, CheckConnectionMessage, null);
                State = ScreenState.Error;
            }

            return;
        }

        if (_inspection is null)
        {
            State = ScreenState.Loading;
        }

        try
        {
            var inspection = await _api.GetInspectionAsync(_jobId, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            _store.Remember(inspection, _clock.GetUtcNow());
            Show(inspection);
            Error = null;
            RefreshError = null;
            State = ScreenState.Content;
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            await GoAsync(Routes.Jobs, new Dictionary<string, object> { [Routes.NoticeParameter] = Routes.JobUnassignedNotice });
        }
        catch (Exception exception) when (IsFailure(exception, token))
        {
            if (_inspection is not null)
            {
                RefreshError = DescribeRefresh(exception, InspectionViewModel.RefreshFailedTitle, CheckConnectionMessage);
                State = ScreenState.Content;
                return;
            }

            Error = Describe(exception, InspectionViewModel.LoadFailedTitle);
            State = ScreenState.Error;
        }
    }

    private void Show(InspectionResponse inspection)
    {
        _inspection = inspection;
        JobNumber = DisplayFormats.JobNumber(inspection.JobNumber);
        PassText = string.Create(CultureInfo.InvariantCulture, $"{inspection.Progress.PassCount} Pass");
        FailText = string.Create(CultureInfo.InvariantCulture, $"{inspection.Progress.FailCount} Fail");
        NotApplicableText = string.Create(CultureInfo.InvariantCulture, $"{inspection.Progress.NotApplicableCount} N/A");
        PassCount = inspection.Progress.PassCount;
        FailCount = inspection.Progress.FailCount;
        NotApplicableCount = inspection.Progress.NotApplicableCount;
        FailedItems = FailedItemsOf(inspection);
        OnPropertyChanged(nameof(HasFailedItems));
        UpdateTimeOnSite();
    }

    private void OnLayoutChanged(object? sender, EventArgs e)
    {
        NotifyColumn();
        FocusInViewRequest++;
    }

    partial void OnIsKeyboardOpenChanged(bool value)
    {
        if (value)
        {
            FocusInViewRequest++;
        }
    }

    private void NotifyColumn()
    {
        OnPropertyChanged(nameof(ColumnMaxWidth));
        OnPropertyChanged(nameof(ColumnOptions));
    }

    private void UpdateTimeOnSite()
    {
        if (_inspection?.StartedAt is not { } started)
        {
            TimeOnSiteText = string.Empty;
            return;
        }

        var now = _clock.GetUtcNow();
        TimeOnSiteText = TimeOnSite(started, now, Zone, DisplayFormats.LocalDay(now, Zone));
    }

    private void OnMinute()
    {
        if (_context is { } context && !ReferenceEquals(SynchronizationContext.Current, context))
        {
            context.Post(_ => UpdateTimeOnSite(), null);
            return;
        }

        UpdateTimeOnSite();
    }

    private void NotifyScreen()
    {
        OnPropertyChanged(nameof(ShowsDock));
        OnPropertyChanged(nameof(ShowsOfflineBanner));
        OnPropertyChanged(nameof(IsSubmitOffline));
        OnPropertyChanged(nameof(HelperText));
        SubmitCommand.NotifyCanExecuteChanged();
    }

    private bool Dropped(Guid? requestedFor) => requestedFor is { } requester && _session.User?.Id != requester;

    private async Task GoAsync(string route, IDictionary<string, object>? parameters)
    {
        try
        {
            if (parameters is null)
            {
                await _navigator.GoToAsync(route);
            }
            else
            {
                await _navigator.GoToAsync(route, parameters);
            }
        }
        catch (Exception exception)
        {
            LogNavigateFailed(_logger, _jobId, exception.GetType().Name);
        }
    }

    private void OnSessionEnded(object? sender, EventArgs e)
    {
        void Clear()
        {
            _inspection = null;
            _jobId = Guid.Empty;
            _applyingNotes = true;
            TechnicianNotes = string.Empty;
            _applyingNotes = false;
            IsConfirmed = false;
            FailedItems = [];
            State = ScreenState.Loading;
        }

        if (_context is { } context && !ReferenceEquals(SynchronizationContext.Current, context))
        {
            context.Post(_ => Clear(), null);
            return;
        }

        Clear();
    }

    [LoggerMessage(EventName = "inspection.submitted", Level = LogLevel.Information, Message = "inspection.submitted {JobId}")]
    private static partial void LogSubmitted(ILogger logger, Guid jobId);

    [LoggerMessage(EventName = "inspection.submit-failed", Level = LogLevel.Warning, Message = "inspection.submit-failed {JobId} {StatusCode}")]
    private static partial void LogSubmitFailed(ILogger logger, Guid jobId, int statusCode);

    [LoggerMessage(EventName = "inspection.review-navigate-failed", Level = LogLevel.Warning, Message = "inspection.review-navigate-failed {JobId} {Failure}")]
    private static partial void LogNavigateFailed(ILogger logger, Guid jobId, string failure);
}
