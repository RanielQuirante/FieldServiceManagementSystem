using FieldService.Api.Domain;
using FieldService.Api.Shared.Errors;
using FieldService.Api.Shared.Http;
using FieldService.Api.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FieldService.Api.Features.Auth;

public static class SessionResults
{
    public static Ok<SessionResponse> Issued(
        HttpContext http,
        AccessTokenIssuer accessTokens,
        SessionUser user,
        Guid sessionId,
        SessionClient client,
        NewRefreshToken refreshToken,
        DateTimeOffset now,
        DateTimeOffset idleExpiresAt)
    {
        var accessToken = accessTokens.Issue(user.Id, user.Role, sessionId);
        string? bodyToken = null;
        if (client == SessionClient.Web)
        {
            RefreshCookie.Set(http, refreshToken.Value, now, idleExpiresAt);
        }
        else
        {
            bodyToken = refreshToken.Value;
        }

        return TypedResults.Ok(new SessionResponse(accessToken.Value, accessToken.ExpiresAt, user, bodyToken));
    }

    public static ProblemHttpResult Expired(HttpContext http, RefreshCredential credential)
    {
        ClearPresentedCookie(http, credential);
        return Problems.Of(http, ErrorCodes.SessionExpired);
    }

    public static void ClearPresentedCookie(HttpContext http, RefreshCredential credential)
    {
        if (credential.PresentedCookie)
        {
            RefreshCookie.Clear(http);
        }
    }

    public static ProblemHttpResult CsrfRequired(HttpContext http) => Problems.Of(http, ErrorCodes.CsrfRequired);
}
