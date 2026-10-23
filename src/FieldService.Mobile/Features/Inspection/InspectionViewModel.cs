using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
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

public enum SaveOutcome
{
    NothingToSave,
    Saved,
    Offline,
    Stayed,
    Left,
}

public sealed partial class InspectionViewModel : ScreenViewModel, IQueryAttributable, IInspectionItemHost
{
    public const string PageTitle = "Inspection";
    public const string BackDescription = "Back to job details";
    public const string LoadingText = "Loading the checklist";
    public const string ReviewText = "Review and submit";
    public const string SavingText = "Saving…";
    public const string LoadFailedTitle = "The checklist could not be loaded.";
    public const string RefreshFailedTitle = "This checklist could not be refreshed.";
    public const string SaveFailedTitle = "Your answers could not be saved.";
    public const string KeptOnDevice = "They are kept on this device.";
    public const string TryAgainSoon = "Try again in a moment.";
    public const string OfflineTitle = "You're offline.";
    public const string OfflineMessage = "Your answers stay on this device until you're back online.";
    public const string OfflineHelper = "You're offline. Connect to review and submit.";
    public const string ConflictMessage = "The latest answers are shown with your changes. Check them, then try again.";
    public const string ReadOnlyMessage = "This checklist is read-only.";
    public const string CancelledTitle = "This job was cancelled.";
    public const string CancelledMessage = "The checklist is read-only.";
    public const string OnHoldTitle = "This job is on hold.";
    public const string OnHoldMessage = "Resume the job to change the checklist.";
    public const string NotSavedTitle = "Your changes were not saved.";
    public const string FinishFirstTitle = "Finish the checklist first.";
    public const string ConcurrencyConflictCode = "concurrency.conflict";
    public const string LockedCode = "inspection.locked";

    private readonly InspectionApiClient _api;
    private readonly InspectionStore _store;
    private readonly ISessionService _session;
    private readonly INavigator _navigator;
    private readonly ISemanticScreenReader _screenReader;
    private readonly IWindowLayout _layout;
    private readonly TimeProvider _clock;
    private readonly ILogger<InspectionViewModel> _logger;
    private CancellationTokenSource? _load;
    private InspectionResponse? _inspection;
    private DateTimeOffset _loadedAt;
    private Guid _jobId;
    private bool _leaveWithoutSaving;
    private bool _refusedSave;
    private string? _pendingNotice;
    private SynchronizationContext? _context;

    public InspectionViewModel(
        InspectionApiClient api,
        InspectionStore store,
        ISessionService session,
        INavigator navigator,
        ISemanticScreenReader screenReader,
        IWindowLayout layout,
        NetworkStatus network,
        TimeProvider clock,
        ILogger<InspectionViewModel> logger)
        : base(network)
    {
        _api = api;
        _store = store;
        _session = session;
        _navigator = navigator;
        _screenReader = screenReader;
        _layout = layout;
        _clock = clock;
        _logger = logger;
        Items = [];
    }

    [ObservableProperty]
    public partial IReadOnlyList<InspectionItemViewModel> Items { get; private set; }

    [ObservableProperty]
    public partial string TemplateName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string JobNumber { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnsweredText), nameof(HelperText), nameof(LeftCount))]
    public partial int AnsweredCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OfAnsweredText), nameof(HelperText), nameof(LeftCount))]
    public partial int Total { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PassText))]
    public partial int PassCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FailText))]
    public partial int FailCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotApplicableText))]
    public partial int NotApplicableCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReviewLabel))]
    public partial bool IsSaving { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsDock), nameof(IsReadOnly))]
    [NotifyCanExecuteChangedFor(nameof(ReviewCommand))]
    public partial bool IsEditable { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSlotBanner))]
    public partial PageBanner? SlotBanner { get; private set; }

    [ObservableProperty]
    public partial int ScrollToTopRequest { get; private set; }

    public Guid JobId => _jobId;

    public InspectionResponse? Inspection => _inspection;

    public double ColumnMaxWidth => DesktopColumn.MaxWidth(_layout);

    public LayoutOptions ColumnOptions => DesktopColumn.Options(_layout);

    public bool HasSlotBanner => SlotBanner is not null;

    public bool IsReadOnly => !IsEditable;

    public bool ShowsHeroCounts => State == ScreenState.Content;

    public bool ShowsLoadingLine => State == ScreenState.Loading;

    public bool ShowsDock => State == ScreenState.Content && IsEditable;

    public bool ShowsOfflineBanner => !IsOnline && State == ScreenState.Content;

    public bool IsReviewOffline => !IsOnline;

    public int LeftCount => Math.Max(0, Total - AnsweredCount);

    public string AnsweredText => AnsweredCount.ToString(CultureInfo.InvariantCulture);

    public string OfAnsweredText => string.Create(CultureInfo.InvariantCulture, $"of {Total} answered");

    public string PassText => string.Create(CultureInfo.InvariantCulture, $"{PassCount} Pass");

    public string FailText => string.Create(CultureInfo.InvariantCulture, $"{FailCount} Fail");

    public string NotApplicableText => string.Create(CultureInfo.InvariantCulture, $"{NotApplicableCount} N/A");

    public string HeroDescription => AnsweredText + " " + OfAnsweredText + ", " + PassText + ", " + FailText + ", " + NotApplicableText;

    public string ReviewLabel => IsSaving ? SavingText : ReviewText;

    public string HelperText => !IsOnline ? OfflineHelper : LeftText(LeftCount, Total);

    public AnswerOutcome AnswerOutcome { get; private set; }

    private TimeZoneInfo Zone => _clock.LocalTimeZone;

    private DateOnly Today => DisplayFormats.LocalDay(_clock.GetUtcNow(), Zone);

    public bool AcceptsInput => !IsSaving && IsEditable;

    public static bool IsEventBanner(PageBanner? banner) => banner is { Variant: not BannerVariant.Locked };

    partial void OnSlotBannerChanged(PageBanner? value)
    {
        if (IsEventBanner(value))
        {
            RefreshError = null;
        }
    }

    public static string LeftText(int left, int total) => left switch
    {
        0 => string.Create(CultureInfo.InvariantCulture, $"All {total} items answered"),
        1 => "1 item left to answer",
        _ => string.Create(CultureInfo.InvariantCulture, $"{left} items left to answer"),
    };

    public static string NeedsDetailText(int count) => count == 1
        ? "1 answer needs more detail."
        : string.Create(CultureInfo.InvariantCulture, $"{count} answers need more detail.");

    public static string NotAnsweredYetText(int count) => count == 1
        ? "1 item is not answered yet."
        : string.Create(CultureInfo.InvariantCulture, $"{count} items are not answered yet.");

    public static AnswerKey? ParseAnswerKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var match = AnswerKeyPattern().Match(key);
        if (!match.Success)
        {
            return null;
        }

        var index = int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture);
        var field = match.Groups["field"].Value.ToLowerInvariant();
        int? reading = match.Groups["reading"].Success ? int.Parse(match.Groups["reading"].Value, CultureInfo.InvariantCulture) : null;
        return new AnswerKey(index, field == "note" ? AnswerKeyField.Note : AnswerKeyField.Readings, reading);
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.TryGetValue(Routes.JobIdParameter, out var value))
        {
            var jobId = value switch
            {
                Guid id => id,
                string text when Guid.TryParse(text, out var parsed) => parsed,
                _ => Guid.Empty,
            };
            if (jobId != Guid.Empty && jobId != _jobId)
            {
                Reset();
                _jobId = jobId;
                if (_store.JobFor(jobId) is { } job)
                {
                    TemplateName = job.TemplateName;
                    JobNumber = DisplayFormats.JobNumber(job.Number);
                }
            }
        }

        if (query.TryGetValue(Routes.NoticeParameter, out var notice) && notice as string == Routes.SubmissionIncompleteNotice)
        {
            _pendingNotice = Routes.SubmissionIncompleteNotice;
            if (IsActive)
            {
                _ = LoadAsync(background: true);
            }
        }
    }

    public void Reset()
    {
        _load?.Cancel();
        _load = null;
        _inspection = null;
        _leaveWithoutSaving = false;
        _refusedSave = false;
        _pendingNotice = null;
        Items = [];
        SlotBanner = null;
        Error = null;
        RefreshError = null;
        IsEditable = false;
        IsSaving = false;
        UpdateCounts();
        State = ScreenState.Loading;
        NotifyScreen();
    }

    public void OnItemEdited(InspectionItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        SyncDraft(item);
        UpdateCounts();
    }

    public void OnResultSelected(InspectionItemViewModel item, InspectionResult result)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (result is InspectionResult.Pass or InspectionResult.NotApplicable)
        {
            _screenReader.Announce(InspectionItemViewModel.ResultLabel(result) + ". " + AnsweredText + " " + OfAnsweredText + ".");
        }
    }

    public bool HandleBackButton()
    {
        _ = BackCommand.ExecuteAsync(null);
        return true;
    }

    protected override Task ReloadAsync(CancellationToken cancellationToken) => RetryLoadAsync();

    protected override void OnActivated()
    {
        _context = SynchronizationContext.Current;
        _session.SessionEnded += OnSessionEnded;
        _layout.Changed += OnLayoutChanged;
        OnLayoutChanged(this, EventArgs.Empty);
    }

    protected override void OnDeactivated()
    {
        _session.SessionEnded -= OnSessionEnded;
        _layout.Changed -= OnLayoutChanged;
    }

    private void OnLayoutChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(ColumnMaxWidth));
        OnPropertyChanged(nameof(ColumnOptions));
    }

    protected override void OnConnectivityChanged(bool isOnline)
    {
        NotifyScreen();
        ReviewCommand.NotifyCanExecuteChanged();
        if (isOnline && State == ScreenState.Error)
        {
            _ = LoadAsync(background: false);
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
        else if (e.PropertyName == nameof(IsSaving))
        {
            ReviewCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private Task AppearingAsync()
    {
        Activate();
        if (_jobId == Guid.Empty)
        {
            return Task.CompletedTask;
        }

        if (_inspection is null && _store.Loaded(_jobId) is { } cached)
        {
            Show(cached.Inspection, cached.LoadedAt);
            State = ScreenState.Content;
        }

        return LoadAsync(background: _inspection is not null);
    }

    [RelayCommand]
    private void Disappearing() => Deactivate();

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task BackAsync()
    {
        if (IsSaving)
        {
            return;
        }

        if (State != ScreenState.Content || !IsEditable || !IsOnline || _leaveWithoutSaving)
        {
            _leaveWithoutSaving = false;
            await LeaveAsync();
            return;
        }

        var outcome = await SaveAsync();
        if (outcome is SaveOutcome.NothingToSave or SaveOutcome.Saved or SaveOutcome.Offline)
        {
            await LeaveAsync();
        }
    }

    private bool CanReview() => IsOnline && IsEditable && State == ScreenState.Content;

    [RelayCommand(CanExecute = nameof(CanReview))]
    private async Task ReviewAsync()
    {
        if (IsSaving)
        {
            return;
        }

        var outcome = await SaveAsync();
        if (outcome is SaveOutcome.Stayed or SaveOutcome.Left or SaveOutcome.Offline)
        {
            return;
        }

        var incomplete = Items.Where(item => item.IsIncompleteAnswer).ToList();
        if (incomplete.Count > 0)
        {
            foreach (var item in incomplete)
            {
                item.RevealErrors();
            }

            incomplete[0].FocusFirstError();
            AnswerOutcome = AnswerOutcome.NeedsDetail;
            _screenReader.Announce(NeedsDetailText(incomplete.Count));
            return;
        }

        var unanswered = Items.Where(item => !item.IsAnswered).ToList();
        if (unanswered.Count > 0)
        {
            unanswered[0].FocusPrompt();
            AnswerOutcome = AnswerOutcome.Unanswered;
            _screenReader.Announce(unanswered.Count == 1 ? "1 item left to answer." : string.Create(CultureInfo.InvariantCulture, $"{unanswered.Count} items left to answer."));
            return;
        }

        AnswerOutcome = AnswerOutcome.Ready;
        try
        {
            await _navigator.GoToAsync(Routes.ReviewPage, new Dictionary<string, object>
            {
                [Routes.JobIdParameter] = _jobId,
                [Routes.SourceParameter] = Routes.FromInspection,
            });
        }
        catch (Exception exception)
        {
            LogNavigateFailed(_logger, _jobId, exception.GetType().Name);
        }
    }

    private async Task RetryLoadAsync()
    {
        var wasError = State == ScreenState.Error;
        if (await LoadAsync(background: _inspection is not null) && wasError)
        {
            Items.FirstOrDefault()?.FocusPrompt();
        }
    }

    private async Task<bool> LoadAsync(bool background)
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
            if (_inspection is null)
            {
                Error = new ScreenError(LoadFailedTitle, CheckConnectionMessage, null);
                State = ScreenState.Error;
            }

            return false;
        }

        if (!background || _inspection is null)
        {
            State = _inspection is null ? ScreenState.Loading : State;
        }

        var requestedFor = _session.User?.Id;
        try
        {
            var inspection = await _api.GetInspectionAsync(_jobId, token);
            if (token.IsCancellationRequested || Dropped(requestedFor))
            {
                return false;
            }

            Show(inspection, _clock.GetUtcNow());
            Error = null;
            RefreshError = null;
            State = ScreenState.Content;
            ConsumeNotice();
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
            LogLoadFailed(_logger, _jobId, exception is ApiException api ? (int)api.StatusCode : 0);
            if (_inspection is not null)
            {
                Error = null;
                RefreshError = DescribeRefresh(exception, RefreshFailedTitle, "Showing it as loaded at " + DisplayFormats.Time(_loadedAt, Zone) + ".");
                if (IsEventBanner(SlotBanner))
                {
                    SlotBanner = null;
                }

                State = ScreenState.Content;
                return false;
            }

            RefreshError = null;
            Error = Describe(exception, LoadFailedTitle);
            State = ScreenState.Error;
            return false;
        }
    }

    private void Show(InspectionResponse inspection, DateTimeOffset loadedAt)
    {
        _inspection = inspection;
        _loadedAt = loadedAt;
        _store.Remember(inspection, loadedAt);
        TemplateName = inspection.Template.Name;
        JobNumber = DisplayFormats.JobNumber(inspection.JobNumber);
        var editable = inspection.Affordances.CanEdit;
        if (inspection.JobStatus is JobStatus.Completed or JobStatus.Cancelled)
        {
            _store.ClearDraft(_jobId);
        }

        var ordered = inspection.Items.OrderBy(item => item.Position).ToList();
        var total = ordered.Count;
        var sameItems = Items.Count == total && Items.Select(item => item.ChecklistItemId).SequenceEqual(ordered.Select(item => item.ChecklistItemId));
        if (sameItems)
        {
            for (var index = 0; index < total; index++)
            {
                var item = Items[index];
                item.ApplySaved(ordered[index], keepEdits: editable && item.IsChanged);
                item.IsEditable = editable;
            }
        }
        else
        {
            var drafts = editable ? _store.Answers(_jobId) : new Dictionary<Guid, DraftAnswer>();
            var items = ordered.Select(item => new InspectionItemViewModel(this, item, total)).ToList();
            foreach (var item in items)
            {
                item.IsEditable = editable;
                if (drafts.TryGetValue(item.ChecklistItemId, out var draft))
                {
                    item.ApplyDraft(draft);
                }
            }

            Items = items;
        }

        IsEditable = editable;
        if (editable)
        {
            _refusedSave = false;
            foreach (var item in Items)
            {
                SyncDraft(item);
            }

            if (SlotBanner?.Variant == BannerVariant.Locked)
            {
                SlotBanner = null;
            }
        }
        else if (!_refusedSave)
        {
            SlotBanner = LockedBanner(inspection, refused: false);
        }

        UpdateCounts();
        NotifyScreen();
    }

    private async Task<SaveOutcome> SaveAsync()
    {
        if (_inspection is not { } inspection || !IsEditable)
        {
            return SaveOutcome.NothingToSave;
        }

        var changed = Items.Where(item => item.IsAnswered && item.IsChanged && item.IsComplete).ToList();
        if (changed.Count == 0)
        {
            return SaveOutcome.NothingToSave;
        }

        if (!IsOnline)
        {
            return SaveOutcome.Offline;
        }

        var request = new SaveInspectionRequest([.. changed.Select(item => item.ToInput())], inspection.Version);
        var requestedFor = _session.User?.Id;
        IsSaving = true;
        try
        {
            var saved = await _api.SaveInspectionAsync(_jobId, request, CancellationToken.None);
            if (Dropped(requestedFor))
            {
                return SaveOutcome.Left;
            }

            _leaveWithoutSaving = false;
            foreach (var item in changed)
            {
                item.HideErrors();
            }

            Show(saved, _clock.GetUtcNow());
            if (SlotBanner?.Variant is BannerVariant.Error or BannerVariant.Conflict)
            {
                SlotBanner = null;
            }

            LogSaved(_logger, _jobId, request.Answers.Count);
            return SaveOutcome.Saved;
        }
        catch (Exception) when (Dropped(requestedFor))
        {
            return SaveOutcome.Left;
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.BadRequest)
        {
            LogSaveFailed(_logger, _jobId, 400);
            ShowValidationErrors(exception, changed);
            return SaveOutcome.Stayed;
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            LogSaveFailed(_logger, _jobId, 404);
            await NoLongerAssignedAsync();
            return SaveOutcome.Left;
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict && exception.Code == LockedCode)
        {
            LogSaveFailed(_logger, _jobId, 409);
            _store.ClearDraft(_jobId);
            _refusedSave = true;
            await LoadAsync(background: true);
            IsEditable = false;
            foreach (var item in Items)
            {
                item.ApplySaved(item.Item, keepEdits: false);
                item.IsEditable = false;
            }

            UpdateCounts();
            SlotBanner = _inspection is { } latest ? LockedBanner(latest, refused: true) : null;
            ScrollToTopRequest++;
            return SaveOutcome.Stayed;
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            LogSaveFailed(_logger, _jobId, 409);
            var reloaded = await LoadAsync(background: true);
            if (reloaded && IsEditable)
            {
                SlotBanner = new PageBanner(BannerVariant.Conflict, JobNumber + " changed after you opened it.", ConflictMessage, null);
                ScrollToTopRequest++;
            }

            return SaveOutcome.Stayed;
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException or OperationCanceledException)
        {
            LogSaveFailed(_logger, _jobId, exception is ApiException api ? (int)api.StatusCode : 0);
            _leaveWithoutSaving = true;
            SlotBanner = DescribeSaveFailure(exception);
            ScrollToTopRequest++;
            return SaveOutcome.Stayed;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void ShowValidationErrors(ApiException exception, IReadOnlyList<InspectionItemViewModel> sent)
    {
        var errors = exception.Problem?.Errors ?? new Dictionary<string, string[]>();
        var placed = new List<InspectionItemViewModel>();
        var unplaced = false;
        foreach (var (key, messages) in errors)
        {
            var message = messages.FirstOrDefault();
            if (message is null)
            {
                continue;
            }

            if (ParseAnswerKey(key) is not { } answerKey || answerKey.Index < 0 || answerKey.Index >= sent.Count)
            {
                unplaced = true;
                continue;
            }

            var item = sent[answerKey.Index];
            if (answerKey.Field == AnswerKeyField.Note)
            {
                item.ShowServerNoteError(message);
            }
            else
            {
                var readings = item.SentReadings();
                var target = answerKey.Reading is { } reading
                    ? reading >= 0 && reading < readings.Count ? readings[reading] : null
                    : item.Readings.FirstOrDefault();
                if (target is null)
                {
                    unplaced = true;
                    continue;
                }

                target.ShowServerError(message);
            }

            placed.Add(item);
        }

        if (placed.Count == 0 || unplaced)
        {
            SlotBanner = new PageBanner(BannerVariant.Error, SaveFailedTitle, TryAgainSoon, exception.CorrelationId);
            ScrollToTopRequest++;
        }

        if (placed.Count > 0)
        {
            Items.Where(placed.Contains).First().FocusFirstError();
        }
    }

    private static PageBanner DescribeSaveFailure(Exception exception) => exception switch
    {
        ApiException { StatusCode: HttpStatusCode.TooManyRequests } api => new PageBanner(BannerVariant.Error, SaveFailedTitle, KeptOnDevice + " " + RetryWait(api.RetryAfter), null),
        ApiException api => new PageBanner(BannerVariant.Error, SaveFailedTitle, KeptOnDevice + " " + TryAgainSoon, api.CorrelationId),
        _ => new PageBanner(BannerVariant.Error, SaveFailedTitle, KeptOnDevice + " " + CheckConnectionMessage, null),
    };

    private PageBanner LockedBanner(InspectionResponse inspection, bool refused)
    {
        if (inspection.JobStatus == JobStatus.Cancelled)
        {
            return refused
                ? new PageBanner(BannerVariant.Locked, NotSavedTitle, "This checklist is read-only because the job was cancelled.", null)
                : new PageBanner(BannerVariant.Locked, CancelledTitle, CancelledMessage, null);
        }

        if (inspection.JobStatus == JobStatus.OnHold && inspection.SubmittedAt is null)
        {
            return refused
                ? new PageBanner(BannerVariant.Locked, NotSavedTitle, "This checklist is read-only because the job is on hold. Resume the job to change it.", null)
                : new PageBanner(BannerVariant.Locked, OnHoldTitle, OnHoldMessage, null);
        }

        var submitted = inspection.SubmittedAt ?? inspection.CompletedAt;
        var moment = submitted is { } at ? DisplayFormats.AtMoment(at, Zone, Today) : null;
        if (refused)
        {
            return new PageBanner(
                BannerVariant.Locked,
                NotSavedTitle,
                moment is null ? "This checklist is read-only because the job was submitted." : "This checklist is read-only because the job was submitted " + moment + ".",
                null);
        }

        return new PageBanner(BannerVariant.Locked, moment is null ? "Submitted." : "Submitted " + moment + ".", ReadOnlyMessage, null);
    }

    private void ConsumeNotice()
    {
        if (_pendingNotice != Routes.SubmissionIncompleteNotice || _inspection is not { } inspection)
        {
            return;
        }

        _pendingNotice = null;
        var left = Math.Max(0, inspection.Progress.Total - inspection.Progress.Answered);
        SlotBanner = new PageBanner(BannerVariant.Conflict, FinishFirstTitle, NotAnsweredYetText(left), null);
        ScrollToTopRequest++;
        Items.FirstOrDefault(item => !item.IsAnswered)?.FocusPrompt();
    }

    private void SyncDraft(InspectionItemViewModel item)
    {
        if (_jobId == Guid.Empty || _inspection is { Affordances.CanEdit: false })
        {
            return;
        }

        if (item.IsChanged)
        {
            _store.Put(_jobId, item.ChecklistItemId, item.ToDraft());
        }
        else
        {
            _store.Remove(_jobId, item.ChecklistItemId);
        }
    }

    private void UpdateCounts()
    {
        Total = Items.Count;
        AnsweredCount = Items.Count(item => item.IsAnswered);
        PassCount = Items.Count(item => item.Result == InspectionResult.Pass);
        FailCount = Items.Count(item => item.Result == InspectionResult.Fail);
        NotApplicableCount = Items.Count(item => item.Result == InspectionResult.NotApplicable);
        OnPropertyChanged(nameof(HeroDescription));
    }

    private void NotifyScreen()
    {
        OnPropertyChanged(nameof(ShowsHeroCounts));
        OnPropertyChanged(nameof(ShowsLoadingLine));
        OnPropertyChanged(nameof(ShowsDock));
        OnPropertyChanged(nameof(ShowsOfflineBanner));
        OnPropertyChanged(nameof(IsReviewOffline));
        OnPropertyChanged(nameof(HelperText));
        ReviewCommand.NotifyCanExecuteChanged();
    }

    private bool Dropped(Guid? requestedFor) => requestedFor is { } requester && _session.User?.Id != requester;

    private async Task LeaveAsync()
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

    private async Task NoLongerAssignedAsync()
    {
        try
        {
            await _navigator.GoToAsync(Routes.Jobs, new Dictionary<string, object> { [Routes.NoticeParameter] = Routes.JobUnassignedNotice });
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

    [GeneratedRegex(@"^answers\[(?<index>[0-9]+)\]\.(?<field>note|readings)(\[(?<reading>[0-9]+)\]\.value)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnswerKeyPattern();

    [LoggerMessage(EventName = "inspection.saved", Level = LogLevel.Information, Message = "inspection.saved {JobId} {AnswerCount}")]
    private static partial void LogSaved(ILogger logger, Guid jobId, int answerCount);

    [LoggerMessage(EventName = "inspection.save-failed", Level = LogLevel.Warning, Message = "inspection.save-failed {JobId} {StatusCode}")]
    private static partial void LogSaveFailed(ILogger logger, Guid jobId, int statusCode);

    [LoggerMessage(EventName = "inspection.load-failed", Level = LogLevel.Warning, Message = "inspection.load-failed {JobId} {StatusCode}")]
    private static partial void LogLoadFailed(ILogger logger, Guid jobId, int statusCode);

    [LoggerMessage(EventName = "inspection.navigate-failed", Level = LogLevel.Warning, Message = "inspection.navigate-failed {JobId} {Failure}")]
    private static partial void LogNavigateFailed(ILogger logger, Guid jobId, string failure);
}

public enum AnswerOutcome
{
    None,
    NeedsDetail,
    Unanswered,
    Ready,
}

public enum AnswerKeyField
{
    Note,
    Readings,
}

public sealed record AnswerKey(int Index, AnswerKeyField Field, int? Reading);
