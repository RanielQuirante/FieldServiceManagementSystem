using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FieldService.Api.Tests.Contracts;

public sealed class SocketTicketVectorTests
{
    public const string ContractHeader = "{\"alg\":\"HS256\",\"typ\":\"JWT\"}";

    public static readonly string[] ClaimOrder = ["iss", "aud", "sub", "role", "jti", "iat", "nbf", "exp"];

    private static readonly string[] _members = ["key", "claims", "token"];

    [Fact]
    public void TheVectorHoldsAThirtyTwoByteKeyTheClaimsInContractOrderAndACompactToken()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(VectorPath));
        var root = document.RootElement;
        var vector = Read();

        Assert.Equal(_members, root.EnumerateObject().Select(member => member.Name));
        Assert.Equal(ClaimOrder, root.GetProperty("claims").EnumerateObject().Select(claim => claim.Name));
        Assert.Equal(32, vector.KeyBytes.Length);
        Assert.Equal(3, vector.Token.Split('.').Length);
        Assert.Equal(AuthOptions.Issuer, vector.Claims.GetProperty("iss").GetString());
        Assert.Equal(SocketTicketIssuer.Audience, vector.Claims.GetProperty("aud").GetString());
        Assert.Equal(UserRole.Administrator.ToString(), vector.Claims.GetProperty("role").GetString());
        Assert.Equal(vector.IssuedAt, vector.Claims.GetProperty("nbf").GetInt64());
        Assert.Equal(vector.IssuedAt + 60, vector.Claims.GetProperty("exp").GetInt64());
        Assert.Equal(7, vector.TicketId.Version);
    }

    [Fact]
    public async Task TheVectorTokenValidatesUnderTheApisDecodingOfTheVectorKeyAndCarriesExactlyTheExpectedClaims()
    {
        var vector = Read();

        var result = await ValidateAsync(vector.Token, vector.KeyBytes, DateTimeOffset.FromUnixTimeSeconds(vector.IssuedAt));

        Assert.True(result.IsValid, result.Exception?.GetType().Name);
        var segments = vector.Token.Split('.');
        Assert.Equal(ContractHeader, Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(segments[0])));
        using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(segments[1]));
        Assert.Equal(ClaimOrder, payload.RootElement.EnumerateObject().Select(claim => claim.Name));
        Assert.True(JsonElement.DeepEquals(vector.Claims, payload.RootElement), "The token payload differs from the vector claims.");
    }

    [Fact]
    public void AnIndependentHmacOverTheVectorHeaderAndPayloadEqualsTheTokenSignature()
    {
        var vector = Read();
        var segments = vector.Token.Split('.');

        var mac = HMACSHA256.HashData(vector.KeyBytes, Encoding.ASCII.GetBytes($"{segments[0]}.{segments[1]}"));

        Assert.Equal(segments[2], Base64UrlEncoder.Encode(mac));
    }

    [Fact]
    public async Task TheApiIssuerGivenTheVectorKeyInstantTicketIdAndSubjectReproducesTheVectorTokenExactly()
    {
        var vector = Read();
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(vector.IssuedAt);

        var ticket = SocketTicketIssuer.Create(vector.KeyBytes, vector.Subject, UserRole.Administrator, vector.TicketId, issuedAt);

        Assert.Equal(vector.Token, ticket.Value);
        Assert.Equal(vector.TicketId, ticket.TicketId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(vector.Claims.GetProperty("exp").GetInt64()), ticket.ExpiresAt);
        Assert.True((await ValidateAsync(ticket.Value, vector.KeyBytes, issuedAt)).IsValid);
    }

    [Fact]
    public async Task TheApiIssuerWritesTheSameHeaderAndClaimsWhenTheInstantCarriesMilliseconds()
    {
        var vector = Read();
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(vector.IssuedAt).AddMilliseconds(999);

        var ticket = SocketTicketIssuer.Create(vector.KeyBytes, vector.Subject, UserRole.Administrator, vector.TicketId, issuedAt);

        Assert.Equal(vector.Token, ticket.Value);
        Assert.True((await ValidateAsync(ticket.Value, vector.KeyBytes, issuedAt)).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task TheVectorTokenIsRefusedWithOneChangedCharacterInAnySegment(int segment)
    {
        var vector = Read();
        var segments = vector.Token.Split('.');
        var characters = segments[segment].ToCharArray();
        var index = characters.Length / 2;
        characters[index] = characters[index] == 'A' ? 'B' : 'A';
        segments[segment] = new string(characters);

        var result = await ValidateAsync(string.Join('.', segments), vector.KeyBytes, DateTimeOffset.FromUnixTimeSeconds(vector.IssuedAt));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task TheVectorTokenIsRefusedUnderAnotherKey()
    {
        var vector = Read();

        var result = await ValidateAsync(vector.Token, RandomNumberGenerator.GetBytes(32), DateTimeOffset.FromUnixTimeSeconds(vector.IssuedAt));

        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenInvalidSignatureException>(result.Exception, exactMatch: false);
    }

    [Fact]
    public void TheVectorKeyIsAPublishedTestKeyThatTheApisStartupRulesRefuse()
    {
        var vector = Read();

        Assert.True(SecretKeyRules.TryDecodeKey(vector.Key, out var key));
        Assert.Equal(SecretKeyRules.PublishedKeyRule, SecretKeyRules.WeakKeyRule(key));
    }

    public static string VectorPath => Path.Combine(RepositoryPaths.Root, "docs", "contracts", "events", "socket-ticket-test-vector.json");

    public static Task<TokenValidationResult> ValidateAsync(string token, byte[] key, DateTimeOffset at) =>
        new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = AuthOptions.Issuer,
            ValidAudience = SocketTicketIssuer.Audience,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            IssuerSigningKey = new SymmetricSecurityKey(key),
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            LifetimeValidator = (notBefore, expires, _, _) =>
                notBefore is { } start && expires is { } end && start <= at.UtcDateTime && at.UtcDateTime < end,
        });

    private static Vector Read()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(VectorPath));
        var root = document.RootElement;
        var claims = root.GetProperty("claims").Clone();
        var key = root.GetProperty("key").GetString()!;
        return new Vector(
            key,
            Convert.FromBase64String(key),
            claims,
            root.GetProperty("token").GetString()!,
            Guid.Parse(claims.GetProperty("sub").GetString()!),
            Guid.Parse(claims.GetProperty("jti").GetString()!),
            claims.GetProperty("iat").GetInt64());
    }

    private sealed record Vector(
        string Key,
        byte[] KeyBytes,
        JsonElement Claims,
        string Token,
        Guid Subject,
        Guid TicketId,
        long IssuedAt)
    {
        public override string ToString() => $"socket ticket test vector {TicketId}";
    }
}
