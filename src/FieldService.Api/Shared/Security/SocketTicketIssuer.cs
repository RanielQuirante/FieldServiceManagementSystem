using System.Buffers;
using System.Text;
using System.Text.Json;
using FieldService.Api.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FieldService.Api.Shared.Security;

public sealed class SocketTicketIssuer(IOptions<AuthOptions> options, TimeProvider time)
{
    public const string Audience = "fieldservice-notifications";

    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private static readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    private readonly Lazy<SigningCredentials> _credentials = new(() =>
        CredentialsFor(Convert.FromBase64String(options.Value.SocketTicketKey ?? string.Empty)));

    public SocketTicket Issue(Guid userId, UserRole role)
    {
        var now = time.GetUtcNow();
        return Create(_credentials.Value, userId, role, Guid.CreateVersion7(now), now);
    }

    public static SocketTicket Create(byte[] key, Guid userId, UserRole role, Guid ticketId, DateTimeOffset issuedAt) =>
        Create(CredentialsFor(key), userId, role, ticketId, issuedAt);

    public static SymmetricSecurityKey SigningKey(byte[] key) => new(key);

    private static SigningCredentials CredentialsFor(byte[] key) =>
        new(SigningKey(key), SecurityAlgorithms.HmacSha256);

    private static SocketTicket Create(
        SigningCredentials credentials,
        Guid userId,
        UserRole role,
        Guid ticketId,
        DateTimeOffset issuedAt)
    {
        var issuedAtSeconds = issuedAt.ToUnixTimeSeconds();
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(issuedAtSeconds) + Lifetime;
        var payload = Payload(userId, role, ticketId, issuedAtSeconds, expiresAt.ToUnixTimeSeconds());
        return new SocketTicket(_handler.CreateToken(payload, credentials), ticketId, expiresAt);
    }

    private static string Payload(Guid userId, UserRole role, Guid ticketId, long issuedAt, long expiresAt)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString(JwtRegisteredClaimNames.Iss, AuthOptions.Issuer);
            writer.WriteString(JwtRegisteredClaimNames.Aud, Audience);
            writer.WriteString(AccessTokenClaims.Subject, userId.ToString());
            writer.WriteString(AccessTokenClaims.Role, role.ToString());
            writer.WriteString(AccessTokenClaims.TokenId, ticketId.ToString());
            writer.WriteNumber(JwtRegisteredClaimNames.Iat, issuedAt);
            writer.WriteNumber(JwtRegisteredClaimNames.Nbf, issuedAt);
            writer.WriteNumber(JwtRegisteredClaimNames.Exp, expiresAt);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
