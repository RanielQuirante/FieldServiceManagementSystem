using FieldService.Api.Domain;
using FieldService.Api.Persistence;
using FieldService.Api.Shared.Http;
using FieldService.Api.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Features.Auth.RefreshSession;

public static class RefreshSessionEndpoint
{
    public static void Map(RouteGroupBuilder auth) =>
        auth.MapPost("/refresh", HandleAsync)
            .WithName("RefreshSession")
            .WithSummary("Refresh a session")
            .WithDescription("Rotates the refresh token and issues a new access token. The Web client sends no body, the fs_refresh cookie and the X-FieldService-Csrf header; the Mobile client sends its refresh token in the body.")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Refresh)
            .WithMetadata(new RequestSizeLimitAttribute(AuthEndpoints.RequestBodyLimit))
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

    public static async Task<Results<Ok<SessionResponse>, ProblemHttpResult>> HandleAsync(
        RefreshSessionRequest? request,
        HttpContext http,
        FieldServiceDbContext db,
        AccessTokenIssuer accessTokens,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var logger = loggers.CreateLogger(typeof(RefreshSessionEndpoint));
        var credential = RefreshCredential.Read(http.Request, request?.RefreshToken);
        if (credential.MissingCsrfHeader)
        {
            SecurityLog.RefreshRejected(logger, SecurityLog.CsrfHeaderMissing, null);
            return SessionResults.CsrfRequired(http);
        }

        if (credential.Token is null)
        {
            return SessionResults.Expired(http, credential);
        }

        if (!RefreshTokenFactory.TryHash(credential.Token, out var hash))
        {
            SecurityLog.RefreshRejected(logger, SecurityLog.UnknownToken, null);
            return SessionResults.Expired(http, credential);
        }

        var presented = await db.RefreshTokens
            .AsNoTracking()
            .Where(token => token.TokenHash == hash)
            .Select(token => new { token.Id, token.SessionId })
            .SingleOrDefaultAsync(cancellationToken);
        if (presented is null)
        {
            SecurityLog.RefreshRejected(logger, SecurityLog.UnknownToken, null);
            return SessionResults.Expired(http, credential);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM fieldservice.user_sessions WHERE id = {presented.SessionId} FOR UPDATE",
            cancellationToken);
        var now = UtcInstant.Truncate(time.GetUtcNow());
        var session = await db.UserSessions.AsNoTracking().SingleAsync(entry => entry.Id == presented.SessionId, cancellationToken);
        var token = await db.RefreshTokens.AsNoTracking().SingleAsync(entry => entry.Id == presented.Id, cancellationToken);
        var user = await db.Users
            .AsNoTracking()
            .Where(entry => entry.Id == session.UserId)
            .Select(entry => new SessionUserState(entry.Id, entry.DisplayName, entry.Role, entry.IsActive))
            .SingleAsync(cancellationToken);

        if (RejectionOf(session, user, credential, now) is { } reason)
        {
            if (reason == SecurityLog.UserInactive)
            {
                await RevokeAsync(db, session.Id, SessionRevocationReason.UserInactive, now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            SecurityLog.RefreshRejected(logger, reason, session.Id);
            return SessionResults.Expired(http, credential);
        }

        var rotated = await RotateAsync(db, token, now, cancellationToken);
        if (rotated.Successor is not { } successor)
        {
            await RevokeAsync(db, session.Id, SessionRevocationReason.RefreshTokenReuse, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            SecurityLog.ReuseDetected(
                logger,
                user.Id,
                session.Id,
                session.Client,
                SecondsSince(token.ConsumedAt, now),
                RateLimitPolicies.ClientAddress(http));
            return SessionResults.Expired(http, credential);
        }

        var idleExpiresAt = AuthOptions.SessionLifetimeFor(session.Client).IdleExpiry(now, session.AbsoluteExpiresAt);
        await db.UserSessions
            .Where(entry => entry.Id == session.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(entry => entry.LastRefreshedAt, now)
                    .SetProperty(entry => entry.IdleExpiresAt, idleExpiresAt),
                cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (rotated.Grace)
        {
            SecurityLog.GraceRetry(logger, user.Id, session.Id, session.Client);
        }

        return SessionResults.Issued(
            http,
            accessTokens,
            new SessionUser(user.Id, user.DisplayName, user.Role),
            session.Id,
            session.Client,
            successor,
            now,
            idleExpiresAt);
    }

    private static string? RejectionOf(
        UserSession session,
        SessionUserState user,
        RefreshCredential credential,
        DateTimeOffset now)
    {
        if (session.Client != credential.Channel)
        {
            return SecurityLog.ClientMismatch;
        }

        if (session.RevokedAt is not null)
        {
            return SecurityLog.SessionRevoked;
        }

        if (now >= session.IdleExpiresAt || now >= session.AbsoluteExpiresAt)
        {
            return SecurityLog.SessionExpired;
        }

        return user.IsActive ? null : SecurityLog.UserInactive;
    }

    private static async Task<Rotation> RotateAsync(
        FieldServiceDbContext db,
        RefreshToken token,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        bool grace;
        if (token.ConsumedAt is null)
        {
            grace = false;
            if (await ConsumeAsync(db, token.Id, now, cancellationToken) != 1)
            {
                return Rotation.Refused;
            }
        }
        else if (token.ReplacedById is { } successorId
            && now - token.ConsumedAt.Value < AuthOptions.RefreshGraceWindow
            && await ConsumeAsync(db, successorId, now, cancellationToken) == 1)
        {
            grace = true;
        }
        else
        {
            return Rotation.Refused;
        }

        var successor = RefreshTokenFactory.Create();
        var row = RefreshToken.Create(token.SessionId, successor.Hash, now);
        db.RefreshTokens.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await db.RefreshTokens
            .Where(entry => entry.Id == token.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.ReplacedById, row.Id), cancellationToken);
        return new Rotation(successor, grace);
    }

    private static Task<int> ConsumeAsync(
        FieldServiceDbContext db,
        Guid tokenId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        db.RefreshTokens
            .Where(entry => entry.Id == tokenId && entry.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.ConsumedAt, now), cancellationToken);

    private static Task<int> RevokeAsync(
        FieldServiceDbContext db,
        Guid sessionId,
        SessionRevocationReason reason,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        db.UserSessions
            .Where(entry => entry.Id == sessionId && entry.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(entry => entry.RevokedAt, now)
                    .SetProperty(entry => entry.RevokedReason, reason),
                cancellationToken);

    private static long SecondsSince(DateTimeOffset? consumedAt, DateTimeOffset now) =>
        consumedAt is { } at ? (long)Math.Floor((now - at).TotalSeconds) : 0;

    private sealed record SessionUserState(Guid Id, string DisplayName, UserRole Role, bool IsActive);

    private sealed record Rotation(NewRefreshToken? Successor, bool Grace)
    {
        public static readonly Rotation Refused = new(null, false);
    }
}
