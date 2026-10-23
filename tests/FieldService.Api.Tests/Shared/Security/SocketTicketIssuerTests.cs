using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FieldService.Api.Tests.Shared.Security;

public sealed class SocketTicketIssuerTests
{
    private const string ContractHeader = "{\"alg\":\"HS256\",\"typ\":\"JWT\"}";

    private static readonly DateTimeOffset _issuedAt = new(2026, 10, 19, 14, 40, 5, 789, TimeSpan.Zero);

    private static readonly string[] _claimOrder = ["iss", "aud", "sub", "role", "jti", "iat", "nbf", "exp"];

    [Fact]
    public void TheTicketCarriesTheContractHeaderAndClaimsInOrderWithASixtySecondLifetimeFromTheInjectedClock()
    {
        var userId = Guid.CreateVersion7();
        var issuer = CreateIssuer(RandomKey(), new FakeTimeProvider(_issuedAt));

        var ticket = issuer.Issue(userId, UserRole.Administrator);

        var segments = ticket.Value.Split('.');
        Assert.Equal(3, segments.Length);
        Assert.Equal(ContractHeader, Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(segments[0])));
        using var claims = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(segments[1]));
        var root = claims.RootElement;
        Assert.Equal(_claimOrder, root.EnumerateObject().Select(claim => claim.Name));
        Assert.Equal("fieldservice-api", root.GetProperty("iss").GetString());
        Assert.Equal("fieldservice-notifications", root.GetProperty("aud").GetString());
        Assert.Equal(userId.ToString(), root.GetProperty("sub").GetString());
        Assert.Equal("Administrator", root.GetProperty("role").GetString());
        Assert.Equal(ticket.TicketId.ToString(), root.GetProperty("jti").GetString());
        Assert.Equal(7, ticket.TicketId.Version);
        Assert.Equal(1792420805, root.GetProperty("iat").GetInt64());
        Assert.Equal(1792420805, root.GetProperty("nbf").GetInt64());
        Assert.Equal(1792420865, root.GetProperty("exp").GetInt64());
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 14, 41, 5, TimeSpan.Zero), ticket.ExpiresAt);
    }

    [Fact]
    public void EveryTicketGetsANewUuidV7TicketId()
    {
        var time = new FakeTimeProvider(_issuedAt);
        var issuer = CreateIssuer(RandomKey(), time);
        var userId = Guid.CreateVersion7();

        var tickets = Enumerable.Range(0, 5).Select(_ => issuer.Issue(userId, UserRole.Administrator)).ToList();

        Assert.Equal(5, tickets.Select(ticket => ticket.TicketId).Distinct().Count());
        Assert.All(tickets, ticket => Assert.Equal(7, ticket.TicketId.Version));
        Assert.Equal(5, tickets.Select(ticket => ticket.Value).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task TheTicketValidatesUnderTheSocketTicketKeyAndNeverUnderTheAccessTokenKey()
    {
        var socketKey = RandomKey();
        var accessKey = RandomKey();
        var issuer = new SocketTicketIssuer(
            Options.Create(new AuthOptions
            {
                AccessTokenKey = Convert.ToBase64String(accessKey),
                SocketTicketKey = Convert.ToBase64String(socketKey),
            }),
            new FakeTimeProvider(_issuedAt));

        var ticket = issuer.Issue(Guid.CreateVersion7(), UserRole.Administrator);

        Assert.True((await ValidateAsync(ticket.Value, socketKey, _issuedAt)).IsValid);
        var underAccessKey = await ValidateAsync(ticket.Value, accessKey, _issuedAt);
        Assert.False(underAccessKey.IsValid);
        Assert.IsType<SecurityTokenInvalidSignatureException>(underAccessKey.Exception, exactMatch: false);
    }

    [Fact]
    public async Task TheTicketIsAcceptedUntilItsExpiryAndRefusedFromIt()
    {
        var key = RandomKey();
        var ticket = CreateIssuer(key, new FakeTimeProvider(_issuedAt)).Issue(Guid.CreateVersion7(), UserRole.Administrator);

        Assert.True((await ValidateAsync(ticket.Value, key, ticket.ExpiresAt.AddSeconds(-1))).IsValid);
        Assert.False((await ValidateAsync(ticket.Value, key, ticket.ExpiresAt)).IsValid);
    }

    [Fact]
    public void TheIssuerReproducesTheVectorTokenByteForByteFromItsKeyInstantSubjectAndTicketId()
    {
        var vector = SocketTicketVector.Load();

        var ticket = SocketTicketIssuer.Create(
            Convert.FromBase64String(vector.Key),
            Guid.Parse(vector.Claim("sub")),
            Enum.Parse<UserRole>(vector.Claim("role")),
            Guid.Parse(vector.Claim("jti")),
            DateTimeOffset.FromUnixTimeSeconds(vector.Number("iat")));

        Assert.Equal(vector.Token, ticket.Value);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(vector.Number("exp")), ticket.ExpiresAt);
    }

    [Fact]
    public async Task TheIssuerConfiguredWithTheVectorKeyIssuesTheVectorHeaderAndClaimsApartFromItsOwnTicketId()
    {
        var vector = SocketTicketVector.Load();
        var issuer = new SocketTicketIssuer(
            Options.Create(new AuthOptions { SocketTicketKey = vector.Key }),
            new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(vector.Number("iat"))));

        var ticket = issuer.Issue(Guid.Parse(vector.Claim("sub")), UserRole.Administrator);

        var segments = ticket.Value.Split('.');
        Assert.Equal(vector.Token.Split('.')[0], segments[0]);
        using var claims = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(segments[1]));
        Assert.Equal(_claimOrder, claims.RootElement.EnumerateObject().Select(claim => claim.Name));
        foreach (var name in _claimOrder.Where(name => name != "jti"))
        {
            Assert.Equal(vector.Claims.GetProperty(name).GetRawText(), claims.RootElement.GetProperty(name).GetRawText());
        }

        var key = Convert.FromBase64String(vector.Key);
        Assert.True((await ValidateAsync(ticket.Value, key, DateTimeOffset.FromUnixTimeSeconds(vector.Number("iat")))).IsValid);
    }

    [Fact]
    public async Task TheVectorTokenValidatesUnderTheApisDecodingOfItsKeyAndCarriesExactlyItsClaims()
    {
        var vector = SocketTicketVector.Load();
        var key = Convert.FromBase64String(vector.Key);
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(vector.Number("iat"));

        var result = await ValidateAsync(vector.Token, key, issuedAt);

        Assert.Equal(32, key.Length);
        Assert.True(result.IsValid, result.Exception?.GetType().Name);
        var segments = vector.Token.Split('.');
        Assert.Equal(ContractHeader, Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(segments[0])));
        Assert.Equal(vector.CompactClaims, Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(segments[1])));
    }

    [Fact]
    public async Task TheVectorTokenIsRefusedWithOneChangedCharacterOrUnderAnotherKey()
    {
        var vector = SocketTicketVector.Load();
        var key = Convert.FromBase64String(vector.Key);
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(vector.Number("iat"));
        var signature = vector.Token[(vector.Token.LastIndexOf('.') + 1)..];
        var changed = vector.Token[..^signature.Length] + (signature[0] == 'A' ? 'B' : 'A') + signature[1..];

        Assert.False((await ValidateAsync(changed, key, issuedAt)).IsValid);
        Assert.False((await ValidateAsync(vector.Token, RandomKey(), issuedAt)).IsValid);
    }

    private static SocketTicketIssuer CreateIssuer(byte[] key, TimeProvider time) =>
        new(Options.Create(new AuthOptions { SocketTicketKey = Convert.ToBase64String(key) }), time);

    private static byte[] RandomKey() => RandomNumberGenerator.GetBytes(32);

    private static Task<TokenValidationResult> ValidateAsync(string token, byte[] key, DateTimeOffset at) =>
        new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "fieldservice-api",
            ValidAudience = "fieldservice-notifications",
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            IssuerSigningKey = new SymmetricSecurityKey(key),
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            LifetimeValidator = (notBefore, expires, _, _) =>
                notBefore <= at.UtcDateTime && expires > at.UtcDateTime,
        });

    private sealed class SocketTicketVector
    {
        private SocketTicketVector(string key, JsonElement claims, string compactClaims, string token)
        {
            Key = key;
            Claims = claims;
            CompactClaims = compactClaims;
            Token = token;
        }

        public string Key { get; }

        public JsonElement Claims { get; }

        public string CompactClaims { get; }

        public string Token { get; }

        public static SocketTicketVector Load()
        {
            var path = Path.Combine(RepositoryPaths.EventContractsDirectory, "socket-ticket-test-vector.json");
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            Assert.Equal(["key", "claims", "token"], root.EnumerateObject().Select(member => member.Name));
            var claims = root.GetProperty("claims").Clone();
            return new SocketTicketVector(
                root.GetProperty("key").GetString()!,
                claims,
                JsonSerializer.Serialize(claims),
                root.GetProperty("token").GetString()!);
        }

        public string Claim(string name) => Claims.GetProperty(name).GetString()!;

        public long Number(string name) => Claims.GetProperty(name).GetInt64();
    }
}
