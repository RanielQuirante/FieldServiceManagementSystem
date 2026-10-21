using FieldService.Api.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FieldService.Api.Shared.Security;

public sealed class AccessTokenIssuer(IOptions<AuthOptions> options, TimeProvider time)
{
    private static readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    private readonly Lazy<SigningCredentials> _credentials = new(() => new SigningCredentials(
        SigningKey(options.Value),
        SecurityAlgorithms.HmacSha256));

    public static SymmetricSecurityKey SigningKey(AuthOptions options) =>
        new(Convert.FromBase64String(options.AccessTokenKey ?? string.Empty));

    public AccessToken Issue(Guid userId, UserRole role, Guid sessionId)
    {
        var now = time.GetUtcNow();
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds());
        var expiresAt = issuedAt + AuthOptions.AccessTokenLifetime;

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = AuthOptions.Issuer,
            Audience = AuthOptions.AccessTokenAudience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = _credentials.Value,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [AccessTokenClaims.Subject] = userId.ToString(),
                [AccessTokenClaims.Role] = role.ToString(),
                [AccessTokenClaims.SessionId] = sessionId.ToString(),
                [AccessTokenClaims.TokenId] = Guid.CreateVersion7(now).ToString(),
            },
        });

        return new AccessToken(token, expiresAt);
    }
}
