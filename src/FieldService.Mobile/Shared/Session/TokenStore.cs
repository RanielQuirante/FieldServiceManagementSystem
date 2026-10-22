using Microsoft.Extensions.Logging;

namespace FieldService.Mobile.Shared.Session;

public sealed partial class TokenStore(ISecureStorage secureStorage, ILogger<TokenStore> logger)
{
    public const string RefreshTokenKey = "fieldservice.refresh-token";

    public async Task<string?> ReadAsync()
    {
        try
        {
            var value = await secureStorage.GetAsync(RefreshTokenKey);
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (Exception exception)
        {
            LogReadFailed(logger, exception.GetType().Name);
            Clear();
            return null;
        }
    }

    public async Task<bool> WriteAsync(string refreshToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);

        try
        {
            await secureStorage.SetAsync(RefreshTokenKey, refreshToken);
            return true;
        }
        catch (Exception exception)
        {
            LogWriteFailed(logger, exception.GetType().Name);
            Clear();
            return false;
        }
    }

    public void Clear()
    {
        try
        {
            secureStorage.RemoveAll();
        }
        catch (Exception exception)
        {
            LogClearFailed(logger, exception.GetType().Name);
        }
    }

    [LoggerMessage(EventName = "session.token-read-failed", Level = LogLevel.Warning, Message = "session.token-read-failed {Failure}")]
    private static partial void LogReadFailed(ILogger logger, string failure);

    [LoggerMessage(EventName = "session.token-write-failed", Level = LogLevel.Warning, Message = "session.token-write-failed {Failure}")]
    private static partial void LogWriteFailed(ILogger logger, string failure);

    [LoggerMessage(EventName = "session.token-clear-failed", Level = LogLevel.Warning, Message = "session.token-clear-failed {Failure}")]
    private static partial void LogClearFailed(ILogger logger, string failure);
}
