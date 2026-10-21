using FieldService.Api.Domain;
using FieldService.Api.Persistence;
using FieldService.Api.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Features.Auth.SignOut;

public static class SignOutEndpoint
{
    public static void Map(RouteGroupBuilder auth) =>
        auth.MapPost("/sign-out", HandleAsync)
            .WithName("SignOut")
            .WithSummary("Sign out")
            .WithDescription("Revokes the session that the refresh credential belongs to. The Web client sends the fs_refresh cookie and the X-FieldService-Csrf header, and the response clears the cookie; the Mobile client sends its refresh token in the body. A missing, unknown, expired or revoked credential also returns 204.")
            .AllowAnonymous()
            .WithMetadata(new RequestSizeLimitAttribute(AuthEndpoints.RequestBodyLimit))
            .ProducesValidationProblem();

    public static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        SignOutRequest? request,
        HttpContext http,
        FieldServiceDbContext db,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var credential = RefreshCredential.Read(http.Request, request?.RefreshToken);
        if (credential.MissingCsrfHeader)
        {
            return SessionResults.CsrfRequired(http);
        }

        if (RefreshTokenFactory.TryHash(credential.Token, out var hash))
        {
            var session = await db.RefreshTokens
                .AsNoTracking()
                .Where(token => token.TokenHash == hash)
                .Join(
                    db.UserSessions,
                    token => token.SessionId,
                    entry => entry.Id,
                    (_, entry) => new { entry.Id, entry.UserId, entry.Client })
                .SingleOrDefaultAsync(cancellationToken);
            if (session is not null && session.Client == credential.Channel)
            {
                var now = UtcInstant.Truncate(time.GetUtcNow());
                var revoked = await db.UserSessions
                    .Where(entry => entry.Id == session.Id && entry.RevokedAt == null)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(entry => entry.RevokedAt, now)
                            .SetProperty(entry => entry.RevokedReason, SessionRevocationReason.SignOut),
                        cancellationToken);
                if (revoked == 1)
                {
                    SecurityLog.SignedOut(loggers.CreateLogger(typeof(SignOutEndpoint)), session.UserId, session.Id);
                }
            }
        }

        SessionResults.ClearPresentedCookie(http, credential);
        return TypedResults.NoContent();
    }
}
