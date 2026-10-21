using FieldService.Api.Domain;
using FieldService.Api.Persistence;
using FieldService.Api.Shared.Errors;
using FieldService.Api.Shared.Http;
using FieldService.Api.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Features.Auth.SignIn;

public static class SignInEndpoint
{
    public static void Map(RouteGroupBuilder auth) =>
        auth.MapPost("/sign-in", HandleAsync)
            .WithName("SignIn")
            .WithSummary("Sign in")
            .WithDescription("Exchanges an email address and password for an access token and a new session. The Web client accepts only administrators and receives the refresh token in the fs_refresh cookie; the Mobile client accepts only technicians and receives it in the body.")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.SignIn)
            .WithMetadata(new RequestSizeLimitAttribute(AuthEndpoints.RequestBodyLimit))
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

    public static async Task<Results<Ok<SessionResponse>, ProblemHttpResult>> HandleAsync(
        SignInRequest request,
        HttpContext http,
        FieldServiceDbContext db,
        PasswordVerifier passwords,
        AccessTokenIssuer accessTokens,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var logger = loggers.CreateLogger(typeof(SignInEndpoint));
        var client = request.Client.GetValueOrDefault();
        var clientIp = RateLimitPolicies.ClientAddress(http);
        var email = request.Email.Trim().ToLowerInvariant();

        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Email == email, cancellationToken);
        var verification = passwords.Verify(user, request.Password);
        if (user is null)
        {
            SecurityLog.SignInFailed(logger, SecurityLog.UnknownAccount, null, client, clientIp);
            return Problems.Of(http, ErrorCodes.InvalidCredentials);
        }

        var failure = verification == PasswordVerificationResult.Failed ? SecurityLog.WrongPassword
            : !user.IsActive ? SecurityLog.InactiveAccount
            : user.Role != RoleFor(client) ? SecurityLog.WrongApplication
            : null;
        if (failure is not null)
        {
            SecurityLog.SignInFailed(logger, failure, user.Id, client, clientIp);
            return Problems.Of(
                http,
                failure == SecurityLog.WrongApplication ? ErrorCodes.WrongApplication : ErrorCodes.InvalidCredentials);
        }

        var now = UtcInstant.Truncate(time.GetUtcNow());
        var lifetime = AuthOptions.SessionLifetimeFor(client);
        var absoluteExpiresAt = now + lifetime.Absolute;
        var idleExpiresAt = lifetime.IdleExpiry(now, absoluteExpiresAt);
        var session = UserSession.Create(user.Id, client, now, idleExpiresAt, absoluteExpiresAt);
        var refreshToken = RefreshTokenFactory.Create();
        db.UserSessions.Add(session);
        db.RefreshTokens.Add(RefreshToken.Create(session.Id, refreshToken.Hash, now));
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.ReplacePasswordHash(passwords.Hash(user, request.Password), now);
        }

        db.Entry(user).Property(signedIn => signedIn.LastSignedInAt).CurrentValue = now;
        await db.SaveChangesAsync(cancellationToken);

        SecurityLog.SignInSucceeded(logger, user.Id, user.Role, client, session.Id);
        return SessionResults.Issued(
            http,
            accessTokens,
            new SessionUser(user.Id, user.DisplayName, user.Role),
            session.Id,
            client,
            refreshToken,
            now,
            idleExpiresAt);
    }

    private static UserRole RoleFor(SessionClient client) =>
        client == SessionClient.Web ? UserRole.Administrator : UserRole.Technician;
}
