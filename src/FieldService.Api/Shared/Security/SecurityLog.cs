using FieldService.Api.Domain;

namespace FieldService.Api.Shared.Security;

public static partial class SecurityLog
{
    public const string SignInSucceededEvent = "auth.sign-in.succeeded";

    public const string SignInFailedEvent = "auth.sign-in.failed";

    public const string GraceRetryEvent = "auth.refresh.grace-retry";

    public const string ReuseDetectedEvent = "auth.refresh.reuse-detected";

    public const string RefreshRejectedEvent = "auth.refresh.rejected";

    public const string SignOutEvent = "auth.sign-out";

    public const string SocketTicketIssuedEvent = "auth.socket-ticket.issued";

    public const string ForbiddenEvent = "authz.forbidden";

    public const string RateLimitRejectedEvent = "rate-limit.rejected";

    public const string UnknownAccount = "unknown-account";

    public const string WrongPassword = "wrong-password";

    public const string InactiveAccount = "inactive-account";

    public const string WrongApplication = "wrong-application";

    public const string UnknownToken = "unknown-token";

    public const string SessionRevoked = "session-revoked";

    public const string SessionExpired = "session-expired";

    public const string UserInactive = "user-inactive";

    public const string ClientMismatch = "client-mismatch";

    public const string CsrfHeaderMissing = "csrf-header-missing";

    [LoggerMessage(
        EventId = 4001,
        EventName = SignInSucceededEvent,
        Level = LogLevel.Information,
        Message = "Sign-in succeeded for user {UserId} with role {Role} on client {Client}, session {SessionId}")]
    public static partial void SignInSucceeded(ILogger logger, Guid userId, UserRole role, SessionClient client, Guid sessionId);

    [LoggerMessage(
        EventId = 4002,
        EventName = SignInFailedEvent,
        Level = LogLevel.Warning,
        Message = "Sign-in failed with reason {Reason} for user {UserId} on client {Client} from {ClientIp}")]
    public static partial void SignInFailed(ILogger logger, string reason, Guid? userId, SessionClient client, string clientIp);

    [LoggerMessage(
        EventId = 4003,
        EventName = GraceRetryEvent,
        Level = LogLevel.Information,
        Message = "Refresh retried within the grace window for user {UserId}, session {SessionId}, client {Client}")]
    public static partial void GraceRetry(ILogger logger, Guid userId, Guid sessionId, SessionClient client);

    [LoggerMessage(
        EventId = 4004,
        EventName = ReuseDetectedEvent,
        Level = LogLevel.Warning,
        Message = "Refresh token reuse revoked session {SessionId} of user {UserId} on client {Client}, {SecondsSinceConsumed} seconds after the token was consumed, from {ClientIp}")]
    public static partial void ReuseDetected(
        ILogger logger,
        Guid userId,
        Guid sessionId,
        SessionClient client,
        long secondsSinceConsumed,
        string clientIp);

    [LoggerMessage(
        EventId = 4005,
        EventName = RefreshRejectedEvent,
        Level = LogLevel.Information,
        Message = "Refresh rejected with reason {Reason} for session {SessionId}")]
    public static partial void RefreshRejected(ILogger logger, string reason, Guid? sessionId);

    [LoggerMessage(
        EventId = 4006,
        EventName = SignOutEvent,
        Level = LogLevel.Information,
        Message = "User {UserId} signed out of session {SessionId}")]
    public static partial void SignedOut(ILogger logger, Guid userId, Guid sessionId);

    [LoggerMessage(
        EventId = 4007,
        EventName = ForbiddenEvent,
        Level = LogLevel.Information,
        Message = "Policy {Policy} refused user {UserId} with role {Role} on {RoutePattern}")]
    public static partial void Forbidden(ILogger logger, string policy, string? userId, string? role, string? routePattern);

    [LoggerMessage(
        EventId = 4008,
        EventName = RateLimitRejectedEvent,
        Level = LogLevel.Warning,
        Message = "Rate limit {Policy} rejected the {PartitionKind} partition of user {UserId} or address {ClientIp} on {RoutePattern}")]
    public static partial void RateLimitRejected(
        ILogger logger,
        string policy,
        string partitionKind,
        string? userId,
        string? clientIp,
        string? routePattern);

    [LoggerMessage(
        EventId = 4009,
        EventName = SocketTicketIssuedEvent,
        Level = LogLevel.Information,
        Message = "Socket ticket {TicketId} issued to user {UserId}")]
    public static partial void SocketTicketIssued(ILogger logger, Guid userId, Guid ticketId);
}
