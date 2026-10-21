using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.Net.Http.Headers;

namespace FieldService.Api.Tests.Security;

[Collection(DatabaseCollection.Name)]
public sealed class AccessTokenValidationTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    public const string ProtectedPath = "/api/checklist-templates";

    private const string Algorithm = "alg";

    private const string NotificationsAudience = "fieldservice-notifications";

    public static TheoryData<string> RefusedTokens =>
    [
        "alg none without a signature",
        "alg none with the HS256 signature",
        "HS256 header with an empty signature",
        "HS512 signed with the access-token key",
        "HS256 signed with another key",
        "another issuer",
        "another audience",
        "expired 31 seconds ago, beyond the 30-second skew",
        "not valid until 31 seconds from now, beyond the skew",
        "no exp claim",
        "role changed from Technician to Administrator after signing",
        "a socket ticket signed with the socket-ticket key",
        "a socket-ticket audience signed with the access-token key",
        "not a JWT",
    ];

    public static TheoryData<string> AcceptedTokens =>
    [
        "valid HS256 token crafted by the test",
        "expired 29 seconds ago, inside the 30-second skew",
        "not valid until 29 seconds from now, inside the skew",
    ];

    [Theory]
    [MemberData(nameof(RefusedTokens))]
    public async Task AForgedOrMisusedBearerTokenGets401WithoutAnErrorDescription(string kind)
    {
        using var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TokenOf(Fixture.Factory, kind);

        using var response = await client.GetAsync(ProtectedPath);

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        AssertChallenge(response, tokenSent: true);
    }

    [Theory]
    [MemberData(nameof(AcceptedTokens))]
    public async Task ACraftedTokenWithTheRightKeyClaimsAndLifetimeIsAccepted(string kind)
    {
        using var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TokenOf(Fixture.Factory, kind);

        using var response = await client.GetAsync(ProtectedPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("access_token")]
    [InlineData("token")]
    [InlineData("bearer")]
    [InlineData("authorization")]
    public async Task AValidTokenInTheQueryStringIsIgnoredAndGets401(string parameter)
    {
        using var client = Fixture.Factory.CreateApiClient();
        var token = TestTokens.For(Fixture.Factory, UserRole.Administrator);

        using var response = await client.GetAsync($"{ProtectedPath}?{parameter}={Uri.EscapeDataString(token)}");

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        AssertChallenge(response, tokenSent: false);
    }

    [Theory]
    [InlineData("access_token")]
    [InlineData("fs_refresh")]
    [InlineData("Authorization")]
    public async Task AValidTokenInACookieIsIgnoredAndGets401(string cookieName)
    {
        using var client = Fixture.Factory.CreateApiClient();
        var token = TestTokens.For(Fixture.Factory, UserRole.Administrator);

        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, ProtectedPath)
        {
            Headers = { { HeaderNames.Cookie, $"{cookieName}={token}" } },
        });

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        AssertChallenge(response, tokenSent: false);
    }

    [Fact]
    public async Task ARequestWithoutATokenGetsABareBearerChallenge()
    {
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.GetAsync(ProtectedPath);

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        AssertChallenge(response, tokenSent: false);
    }

    public static string TokenOf(ApiFactory factory, string kind)
    {
        var accessKey = Convert.FromBase64String(factory.Settings[AuthOptions.AccessTokenKeyName]!);
        var socketKey = Convert.FromBase64String(factory.Settings[AuthOptions.SocketTicketKeyName]!);
        var now = factory.Clock.GetUtcNow();
        var claims = Claims(now, UserRole.Administrator);
        return kind switch
        {
            "valid HS256 token crafted by the test" => Encode(Header("HS256"), claims, accessKey),
            "expired 29 seconds ago, inside the 30-second skew" =>
                Encode(Header("HS256"), WithLifetime(claims, now.AddMinutes(-15), now.AddSeconds(-29)), accessKey),
            "not valid until 29 seconds from now, inside the skew" =>
                Encode(Header("HS256"), WithNotBefore(claims, now.AddSeconds(29)), accessKey),
            "alg none without a signature" => Unsigned(Header("none"), claims),
            "alg none with the HS256 signature" => SwapHeader(Encode(Header("HS256"), claims, accessKey), Header("none")),
            "HS256 header with an empty signature" => Unsigned(Header("HS256"), claims),
            "HS512 signed with the access-token key" => Encode(Header("HS512"), claims, accessKey),
            "HS256 signed with another key" => Encode(Header("HS256"), claims, RandomNumberGenerator.GetBytes(32)),
            "another issuer" => Encode(Header("HS256"), With(claims, "iss", NotificationsAudience), accessKey),
            "another audience" => Encode(Header("HS256"), With(claims, "aud", "fieldservice-web"), accessKey),
            "expired 31 seconds ago, beyond the 30-second skew" =>
                Encode(Header("HS256"), WithLifetime(claims, now.AddMinutes(-15), now.AddSeconds(-31)), accessKey),
            "not valid until 31 seconds from now, beyond the skew" =>
                Encode(Header("HS256"), WithNotBefore(claims, now.AddSeconds(31)), accessKey),
            "no exp claim" => Encode(Header("HS256"), Without(claims, "exp"), accessKey),
            "role changed from Technician to Administrator after signing" => ChangeRole(
                TestTokens.For(factory, UserRole.Technician),
                UserRole.Administrator),
            "a socket ticket signed with the socket-ticket key" =>
                Encode(Header("HS256"), SocketTicketClaims(now), socketKey),
            "a socket-ticket audience signed with the access-token key" =>
                Encode(Header("HS256"), SocketTicketClaims(now), accessKey),
            "not a JWT" => "not-a-token",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown token kind."),
        };
    }

    public static string ForgedToken(DateTimeOffset now, UserRole role = UserRole.Administrator) =>
        Encode(Header("HS256"), Claims(now, role), RandomNumberGenerator.GetBytes(32));

    public static Dictionary<string, object?> Claims(DateTimeOffset now, UserRole role)
    {
        var issuedAt = now.ToUnixTimeSeconds();
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["iss"] = AuthOptions.Issuer,
            ["aud"] = AuthOptions.AccessTokenAudience,
            ["sub"] = Guid.CreateVersion7().ToString(),
            ["role"] = role.ToString(),
            ["sid"] = Guid.CreateVersion7().ToString(),
            ["jti"] = Guid.CreateVersion7().ToString(),
            ["iat"] = issuedAt,
            ["nbf"] = issuedAt,
            ["exp"] = issuedAt + (long)AuthOptions.AccessTokenLifetime.TotalSeconds,
        };
    }

    public static string Encode(Dictionary<string, object?> header, Dictionary<string, object?> payload, byte[] key)
    {
        var signingInput = $"{Segment(header)}.{Segment(payload)}";
        var data = Encoding.ASCII.GetBytes(signingInput);
        var signature = (string)header[Algorithm]! switch
        {
            "HS256" => HMACSHA256.HashData(key, data),
            "HS512" => HMACSHA512.HashData(key, data),
            var other => throw new ArgumentOutOfRangeException(nameof(header), other, "Unsupported algorithm."),
        };
        return $"{signingInput}.{Base64Url.EncodeToString(signature)}";
    }

    internal static void AssertChallenge(HttpResponseMessage response, bool tokenSent)
    {
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Equal(tokenSent ? "error=\"invalid_token\"" : null, challenge.Parameter);
        var raw = string.Join(", ", response.Headers.GetValues(HeaderNames.WWWAuthenticate));
        Assert.DoesNotContain("error_description", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error_uri", raw, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, object?> Header(string algorithm) =>
        new(StringComparer.Ordinal) { [Algorithm] = algorithm, ["typ"] = "JWT" };

    private static Dictionary<string, object?> SocketTicketClaims(DateTimeOffset now)
    {
        var issuedAt = now.ToUnixTimeSeconds();
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["iss"] = AuthOptions.Issuer,
            ["aud"] = NotificationsAudience,
            ["sub"] = Guid.CreateVersion7().ToString(),
            ["role"] = nameof(UserRole.Administrator),
            ["jti"] = Guid.CreateVersion7().ToString(),
            ["iat"] = issuedAt,
            ["nbf"] = issuedAt,
            ["exp"] = issuedAt + 60,
        };
    }

    private static Dictionary<string, object?> With(Dictionary<string, object?> claims, string name, object? value) =>
        new(claims, StringComparer.Ordinal) { [name] = value };

    private static Dictionary<string, object?> Without(Dictionary<string, object?> claims, string name)
    {
        var copy = new Dictionary<string, object?>(claims, StringComparer.Ordinal);
        copy.Remove(name);
        return copy;
    }

    private static Dictionary<string, object?> WithLifetime(
        Dictionary<string, object?> claims,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        var copy = With(claims, "iat", issuedAt.ToUnixTimeSeconds());
        copy["nbf"] = issuedAt.ToUnixTimeSeconds();
        copy["exp"] = expiresAt.ToUnixTimeSeconds();
        return copy;
    }

    private static Dictionary<string, object?> WithNotBefore(Dictionary<string, object?> claims, DateTimeOffset notBefore)
    {
        var copy = With(claims, "nbf", notBefore.ToUnixTimeSeconds());
        copy["exp"] = notBefore.Add(AuthOptions.AccessTokenLifetime).ToUnixTimeSeconds();
        return copy;
    }

    private static string Unsigned(Dictionary<string, object?> header, Dictionary<string, object?> payload) =>
        $"{Segment(header)}.{Segment(payload)}.";

    private static string SwapHeader(string token, Dictionary<string, object?> header)
    {
        var parts = token.Split('.');
        return $"{Segment(header)}.{parts[1]}.{parts[2]}";
    }

    private static string ChangeRole(string token, UserRole role)
    {
        var parts = token.Split('.');
        var payload = JsonNode.Parse(Base64Url.DecodeFromChars(parts[1]))!.AsObject();
        Assert.Equal(nameof(UserRole.Technician), payload["role"]!.GetValue<string>());
        payload["role"] = role.ToString();
        return $"{parts[0]}.{Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload.ToJsonString()))}.{parts[2]}";
    }

    private static string Segment(Dictionary<string, object?> value) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(value));
}
