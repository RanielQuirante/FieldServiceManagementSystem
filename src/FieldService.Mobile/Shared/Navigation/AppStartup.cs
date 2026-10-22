using FieldService.Mobile.Shared.Session;
using Microsoft.Extensions.Logging;

namespace FieldService.Mobile.Shared.Navigation;

public sealed partial class AppStartup(ISessionService session, INavigator navigator, TimeProvider timeProvider, ILogger<AppStartup> logger)
{
    public static readonly TimeSpan MaximumHold = TimeSpan.FromSeconds(20);

    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly long _created = timeProvider.GetTimestamp();
    private readonly Lock _gate = new();
    private readonly HashSet<string> _shown = new(StringComparer.Ordinal);
    private string? _awaitedRoute;
    private TaskCompletionSource? _awaitedShown;
    private int _started;

    public Task Completed => _completed.Task;

    public bool IsComplete => _completed.Task.IsCompleted;

    public bool HoldsSplash => !IsComplete && timeProvider.GetElapsedTime(_created) < MaximumHold;

    public void ReportShown(string route)
    {
        ArgumentException.ThrowIfNullOrEmpty(route);

        TaskCompletionSource? release = null;
        lock (_gate)
        {
            _shown.Add(route);
            if (string.Equals(_awaitedRoute, route, StringComparison.Ordinal))
            {
                release = _awaitedShown;
            }
        }

        release?.TrySetResult();
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            await _completed.Task;
            return;
        }

        try
        {
            var route = await session.TryRestoreAsync(cancellationToken);
            if (route != Routes.SignIn)
            {
                await navigator.GoToAsync(route);
            }

            await WhenShownAsync(route, cancellationToken);
        }
        catch (Exception exception)
        {
            LogStartupFailed(logger, exception.GetType().Name);
        }
        finally
        {
            _completed.TrySetResult();
        }
    }

    private async Task WhenShownAsync(string route, CancellationToken cancellationToken)
    {
        TaskCompletionSource shown;
        lock (_gate)
        {
            if (_shown.Contains(route))
            {
                return;
            }

            shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _awaitedRoute = route;
            _awaitedShown = shown;
        }

        var remaining = MaximumHold - timeProvider.GetElapsedTime(_created);
        try
        {
            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException();
            }

            await shown.Task.WaitAsync(remaining, timeProvider, cancellationToken);
        }
        catch (TimeoutException)
        {
            LogScreenNotShown(logger, route);
        }
    }

    [LoggerMessage(EventName = "app.startup-failed", Level = LogLevel.Error, Message = "app.startup-failed {Failure}")]
    private static partial void LogStartupFailed(ILogger logger, string failure);

    [LoggerMessage(EventName = "app.startup-screen-not-shown", Level = LogLevel.Warning, Message = "app.startup-screen-not-shown {Route}")]
    private static partial void LogScreenNotShown(ILogger logger, string route);
}
