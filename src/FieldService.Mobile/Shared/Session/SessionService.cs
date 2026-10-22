using System.Net;
using FieldService.Mobile.Features.SignIn;
using FieldService.Mobile.Shared.Http;
using FieldService.Mobile.Shared.Navigation;
using Microsoft.Extensions.Logging;

namespace FieldService.Mobile.Shared.Session;

public sealed partial class SessionService(
    AuthApiClient auth,
    TokenStore tokens,
    INavigator navigator,
    ILogger<SessionService> logger) : ISessionService
{
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _commit = new(1, 1);
    private Task<bool>? _refresh;
    private SignedInUser? _user;
    private string? _accessToken;
    private SessionEndReason _endReason;
    private long _epoch;

    public event EventHandler? SessionEnded;

    public SignedInUser? User
    {
        get
        {
            lock (_gate)
            {
                return _user;
            }
        }
    }

    public string? AccessToken
    {
        get
        {
            lock (_gate)
            {
                return _accessToken;
            }
        }
    }

    public SessionEndReason EndReason
    {
        get
        {
            lock (_gate)
            {
                return _endReason;
            }
        }
    }

    public async Task<SignedInUser> SignInAsync(string email, string password, CancellationToken cancellationToken)
    {
        var response = await auth.SignInAsync(new SignInRequest(email, password, SessionClient.Mobile), cancellationToken);
        var user = await BeginAsync(response, email);
        LogSignedIn(logger, user.Id);
        return user;
    }

    public async Task<string> TryRestoreAsync(CancellationToken cancellationToken)
    {
        var epoch = CurrentEpoch();
        var refreshToken = await tokens.ReadAsync();
        if (refreshToken is null)
        {
            return Routes.SignIn;
        }

        try
        {
            var response = await auth.RefreshAsync(refreshToken, cancellationToken);
            var user = await RenewAsync(response, epoch);
            if (user is null)
            {
                LogRestoreSuperseded(logger);
                return RouteForCurrentSession();
            }

            LogRestored(logger, user.Id);
            return Routes.Jobs;
        }
        catch (ApiException exception) when (IsRejected(exception))
        {
            LogRestoreRejected(logger, (int)exception.StatusCode);
            return await ClearSessionAsync(SessionEndReason.Expired, epoch) is null
                ? RouteForCurrentSession()
                : Routes.SignIn;
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            LogRestoreDeferred(logger, Describe(exception));
            return Routes.Jobs;
        }
    }

    public async Task<bool> RefreshAccessTokenAsync(string? rejectedAccessToken, CancellationToken cancellationToken)
    {
        Task<bool> refresh;
        TaskCompletionSource<bool>? started = null;
        long epoch;
        lock (_gate)
        {
            if (_accessToken is not null && !string.Equals(_accessToken, rejectedAccessToken, StringComparison.Ordinal))
            {
                return true;
            }

            if (_refresh is null)
            {
                started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _refresh = started.Task;
            }

            refresh = _refresh;
            epoch = _epoch;
        }

        if (started is not null)
        {
            _ = RunRefreshAsync(started, epoch);
        }

        return await refresh.WaitAsync(cancellationToken);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        var userId = User?.Id;
        var refreshToken = await tokens.ReadAsync();
        try
        {
            if (refreshToken is not null)
            {
                await auth.SignOutAsync(refreshToken, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException or OperationCanceledException)
        {
            LogSignOutRequestFailed(logger, Describe(exception));
        }

        LogSignedOut(logger, userId);
        await EndSessionAsync(SessionEndReason.SignedOut, null);
    }

    private static bool IsRejected(ApiException exception) =>
        exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest;

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is ApiException or HttpRequestException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private static string Describe(Exception exception) =>
        exception is ApiException api ? ((int)api.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture) : exception.GetType().Name;

    private long CurrentEpoch()
    {
        lock (_gate)
        {
            return _epoch;
        }
    }

    private string RouteForCurrentSession() => User is null ? Routes.SignIn : Routes.Jobs;

    private async Task RunRefreshAsync(TaskCompletionSource<bool> completion, long epoch)
    {
        try
        {
            var refreshed = await RefreshCoreAsync(epoch);
            Release(completion.Task);
            completion.SetResult(refreshed);
        }
        catch (Exception exception)
        {
            Release(completion.Task);
            completion.SetException(exception);
        }
    }

    private void Release(Task<bool> refresh)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_refresh, refresh))
            {
                _refresh = null;
            }
        }
    }

    private async Task<bool> RefreshCoreAsync(long epoch)
    {
        var refreshToken = await tokens.ReadAsync();
        if (refreshToken is null)
        {
            await ExpireAsync(epoch);
            return false;
        }

        try
        {
            var response = await auth.RefreshAsync(refreshToken, CancellationToken.None);
            var user = await RenewAsync(response, epoch);
            if (user is null)
            {
                LogRefreshSuperseded(logger);
                return false;
            }

            LogRefreshed(logger, user.Id);
            return true;
        }
        catch (ApiException exception) when (IsRejected(exception))
        {
            await ExpireAsync(epoch);
            return false;
        }
    }

    private async Task ExpireAsync(long epoch)
    {
        if (await EndSessionAsync(SessionEndReason.Expired, epoch) is { } ended)
        {
            LogSessionExpired(logger, ended.UserId);
        }
        else
        {
            LogRefreshSuperseded(logger);
        }
    }

    private async Task<SignedInUser> BeginAsync(SessionResponse response, string email)
    {
        await _commit.WaitAsync();
        try
        {
            lock (_gate)
            {
                _epoch++;
                _refresh = null;
            }

            return await StoreAsync(response, email);
        }
        finally
        {
            _commit.Release();
        }
    }

    private async Task<SignedInUser?> RenewAsync(SessionResponse response, long epoch)
    {
        await _commit.WaitAsync();
        try
        {
            lock (_gate)
            {
                if (_epoch != epoch)
                {
                    return null;
                }
            }

            return await StoreAsync(response, null);
        }
        finally
        {
            _commit.Release();
        }
    }

    private async Task<SignedInUser> StoreAsync(SessionResponse response, string? email)
    {
        if (!string.IsNullOrEmpty(response.RefreshToken))
        {
            await tokens.WriteAsync(response.RefreshToken);
        }

        lock (_gate)
        {
            email ??= _user is { } current && current.Id == response.User.Id ? current.Email : null;
            var user = new SignedInUser(response.User.Id, response.User.DisplayName, response.User.Role, email);
            _user = user;
            _accessToken = response.AccessToken;
            _endReason = SessionEndReason.None;
            return user;
        }
    }

    private async Task<EndedSession?> ClearSessionAsync(SessionEndReason reason, long? epoch)
    {
        EndedSession ended;
        await _commit.WaitAsync();
        try
        {
            lock (_gate)
            {
                if (epoch is { } expected && expected != _epoch)
                {
                    return null;
                }

                ended = new EndedSession(_user?.Id);
                _user = null;
                _accessToken = null;
                _endReason = reason;
                _epoch++;
                _refresh = null;
            }

            tokens.Clear();
        }
        finally
        {
            _commit.Release();
        }

        RaiseSessionEnded();
        return ended;
    }

    private void RaiseSessionEnded()
    {
        try
        {
            SessionEnded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            LogEndedHandlerFailed(logger, exception.GetType().Name);
        }
    }

    private async Task<EndedSession?> EndSessionAsync(SessionEndReason reason, long? epoch)
    {
        var ended = await ClearSessionAsync(reason, epoch);
        if (ended is null)
        {
            return null;
        }

        try
        {
            await navigator.GoToAsync(Routes.SignIn);
        }
        catch (Exception exception)
        {
            LogNavigationFailed(logger, exception.GetType().Name);
        }

        return ended;
    }

    [LoggerMessage(EventName = "session.signed-in", Level = LogLevel.Information, Message = "session.signed-in {UserId}")]
    private static partial void LogSignedIn(ILogger logger, Guid userId);

    [LoggerMessage(EventName = "session.restored", Level = LogLevel.Information, Message = "session.restored {UserId}")]
    private static partial void LogRestored(ILogger logger, Guid userId);

    [LoggerMessage(EventName = "session.restore-rejected", Level = LogLevel.Information, Message = "session.restore-rejected {StatusCode}")]
    private static partial void LogRestoreRejected(ILogger logger, int statusCode);

    [LoggerMessage(EventName = "session.restore-deferred", Level = LogLevel.Warning, Message = "session.restore-deferred {Failure}")]
    private static partial void LogRestoreDeferred(ILogger logger, string failure);

    [LoggerMessage(EventName = "session.restore-superseded", Level = LogLevel.Information, Message = "session.restore-superseded")]
    private static partial void LogRestoreSuperseded(ILogger logger);

    [LoggerMessage(EventName = "session.refreshed", Level = LogLevel.Information, Message = "session.refreshed {UserId}")]
    private static partial void LogRefreshed(ILogger logger, Guid userId);

    [LoggerMessage(EventName = "session.refresh-superseded", Level = LogLevel.Information, Message = "session.refresh-superseded")]
    private static partial void LogRefreshSuperseded(ILogger logger);

    [LoggerMessage(EventName = "session.expired", Level = LogLevel.Information, Message = "session.expired {UserId}")]
    private static partial void LogSessionExpired(ILogger logger, Guid? userId);

    [LoggerMessage(EventName = "session.sign-out-request-failed", Level = LogLevel.Warning, Message = "session.sign-out-request-failed {Failure}")]
    private static partial void LogSignOutRequestFailed(ILogger logger, string failure);

    [LoggerMessage(EventName = "session.signed-out", Level = LogLevel.Information, Message = "session.signed-out {UserId}")]
    private static partial void LogSignedOut(ILogger logger, Guid? userId);

    [LoggerMessage(EventName = "session.ended-handler-failed", Level = LogLevel.Warning, Message = "session.ended-handler-failed {Failure}")]
    private static partial void LogEndedHandlerFailed(ILogger logger, string failure);

    [LoggerMessage(EventName = "session.navigation-failed", Level = LogLevel.Warning, Message = "session.navigation-failed {Failure}")]
    private static partial void LogNavigationFailed(ILogger logger, string failure);

    private sealed record EndedSession(Guid? UserId);
}
