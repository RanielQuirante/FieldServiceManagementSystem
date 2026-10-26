using System.ComponentModel;
using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FieldService.Mobile.Shared.Connectivity;
using FieldService.Mobile.Shared.Http;

namespace FieldService.Mobile.Shared.Screens;

public abstract partial class ScreenViewModel : ObservableObject
{
    public const string CheckConnectionMessage = "Check your connection, then try again.";

    private const int DefaultRetryAfterSeconds = 60;

    private readonly NetworkStatus _network;
    private bool _active;
    private bool _retrying;
    private bool _contentFocusPending;

    protected ScreenViewModel(NetworkStatus network)
    {
        ArgumentNullException.ThrowIfNull(network);
        _network = network;
        State = ScreenState.Loading;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading), nameof(IsContent), nameof(IsEmpty), nameof(IsError), nameof(IsOffline), nameof(ShowsContent), nameof(HasRefreshError))]
    public partial ScreenState State { get; protected set; }

    [ObservableProperty]
    public partial ScreenError? Error { get; protected set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRefreshError))]
    public partial ScreenError? RefreshError { get; protected set; }

    public bool IsLoading => State == ScreenState.Loading;

    public bool IsContent => State == ScreenState.Content;

    public bool IsEmpty => State == ScreenState.Empty;

    public bool IsError => State == ScreenState.Error;

    public bool IsOffline => State == ScreenState.Offline;

    public bool ShowsContent => State is ScreenState.Content or ScreenState.Offline;

    public bool HasRefreshError => RefreshError is not null && State is ScreenState.Content or ScreenState.Empty;

    protected bool IsOnline => _network.IsOnline;

    protected bool IsActive => _active;

    public static string RetryWait(TimeSpan? retryAfter)
    {
        var seconds = retryAfter is { } wait ? Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)) : DefaultRetryAfterSeconds;
        return seconds == 1
            ? "Try again in 1 second."
            : string.Create(CultureInfo.InvariantCulture, $"Try again in {seconds} seconds.");
    }

    public static ScreenError Describe(Exception exception, string title)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            ApiException { StatusCode: HttpStatusCode.TooManyRequests } api => new ScreenError(title, RetryWait(api.RetryAfter), api.CorrelationId),
            ApiException api => new ScreenError(title, CheckConnectionMessage, api.CorrelationId),
            _ => new ScreenError(title, CheckConnectionMessage, null),
        };
    }

    public static ScreenError DescribeRefresh(Exception exception, string title, string message)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new ScreenError(title, message, exception is ApiException api ? api.CorrelationId : null);
    }

    public static bool IsFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is ApiException or HttpRequestException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    public void Activate()
    {
        if (_active)
        {
            return;
        }

        _active = true;
        _network.PropertyChanged += OnNetworkPropertyChanged;
        OnActivated();
    }

    public void Deactivate()
    {
        if (!_active)
        {
            return;
        }

        _active = false;
        _network.PropertyChanged -= OnNetworkPropertyChanged;
        OnDeactivated();
    }

    protected virtual void OnActivated()
    {
    }

    protected virtual void OnDeactivated()
    {
    }

    protected virtual void OnConnectivityChanged(bool isOnline)
    {
    }

    protected abstract Task ReloadAsync(CancellationToken cancellationToken);

    protected virtual void RequestContentFocus()
    {
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RetryAsync(CancellationToken cancellationToken)
    {
        if (_retrying)
        {
            return;
        }

        _retrying = true;
        try
        {
            await ReloadAsync(cancellationToken);
        }
        finally
        {
            _retrying = false;
        }
    }

    [RelayCommand]
    private void BannerLeftWithFocus()
    {
        if (State is ScreenState.Content or ScreenState.Empty)
        {
            FulfilContentFocus();
        }
        else
        {
            _contentFocusPending = State == ScreenState.Loading;
        }
    }

    partial void OnStateChanged(ScreenState value)
    {
        if (!_contentFocusPending)
        {
            return;
        }

        if (value is ScreenState.Content or ScreenState.Empty)
        {
            FulfilContentFocus();
        }
        else if (value is ScreenState.Error or ScreenState.Offline)
        {
            _contentFocusPending = false;
        }
    }

    private void FulfilContentFocus()
    {
        _contentFocusPending = false;
        RequestContentFocus();
    }

    private void OnNetworkPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NetworkStatus.IsOnline))
        {
            OnConnectivityChanged(_network.IsOnline);
        }
    }
}
