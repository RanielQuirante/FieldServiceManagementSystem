using System.Security.Claims;
using FieldService.Api.Domain;

namespace FieldService.Api.Shared.Security;

public sealed record CurrentUser(Guid Id, UserRole Role, Guid SessionId)
{
    public static ValueTask<CurrentUser?> BindAsync(HttpContext context) => ValueTask.FromResult(From(context.User));

    public static CurrentUser? From(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true
            || !Guid.TryParse(principal.FindFirstValue(AccessTokenClaims.Subject), out var id)
            || !TryParseRole(principal.FindFirstValue(AccessTokenClaims.Role), out var role)
            || !Guid.TryParse(principal.FindFirstValue(AccessTokenClaims.SessionId), out var sessionId))
        {
            return null;
        }

        return new CurrentUser(id, role, sessionId);
    }

    private static bool TryParseRole(string? value, out UserRole role)
    {
        role = default;
        foreach (var candidate in Enum.GetValues<UserRole>())
        {
            if (string.Equals(candidate.ToString(), value, StringComparison.Ordinal))
            {
                role = candidate;
                return true;
            }
        }

        return false;
    }
}
