using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;

namespace FieldService.Api.Tests.Shared.Security;

[Collection(DatabaseCollection.Name)]
public sealed class AccessTokenIssuerTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private static readonly DateTimeOffset _issuedAt = new(2026, 10, 19, 14, 40, 0, 789, TimeSpan.Zero);

    [Fact]
    public void TheTokenCarriesTheContractHeaderAndClaimsWithAFifteenMinuteLifetimeFromTheInjectedClock()
    {
        var userId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();
        var issuer = CreateIssuer(RandomKey(), new FakeTimeProvider(_issuedAt));

        var token = issuer.Issue(userId, UserRole.Administrator, sessionId);

        var jwt = new JsonWebToken(token.Value);
        Assert.Equal("HS256", jwt.Alg);
        Assert.Equal("JWT", jwt.Typ);
        Assert.False(jwt.TryGetHeaderValue<string>("kid", out _));
        Assert.Equal("fieldservice-api", jwt.Issuer);
        Assert.Equal(["fieldservice-api"], jwt.Audiences);
        Assert.Equal(userId.ToString(), jwt.Subject);
        Assert.Equal("Administrator", jwt.GetClaim("role").Value);
        Assert.Equal(sessionId.ToString(), jwt.GetClaim("sid").Value);
        Assert.Equal(7, Guid.Parse(jwt.Id).Version);
        Assert.Equal(1792420800, jwt.GetPayloadValue<long>("iat"));
        Assert.Equal(1792420800, jwt.GetPayloadValue<long>("nbf"));
        Assert.Equal(1792420800 + 900, jwt.GetPayloadValue<long>("exp"));
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 14, 55, 0, TimeSpan.Zero), token.ExpiresAt);
        Assert.Equal(
            ["aud", "exp", "iat", "iss", "jti", "nbf", "role", "sid", "sub"],
            jwt.Claims.Select(claim => claim.Type).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheTokenIsSignedWithTheAccessTokenKeyAndEachTokenHasItsOwnId()
    {
        var accessTokenKey = RandomKey();
        var otherKey = RandomKey();
        var issuer = CreateIssuer(accessTokenKey, new FakeTimeProvider(_issuedAt));
        var handler = new JsonWebTokenHandler();

        var first = issuer.Issue(Guid.CreateVersion7(), UserRole.Technician, Guid.CreateVersion7());
        var second = issuer.Issue(Guid.CreateVersion7(), UserRole.Technician, Guid.CreateVersion7());

        var valid = await handler.ValidateTokenAsync(first.Value, SignatureOnly(accessTokenKey));
        var wrongKey = await handler.ValidateTokenAsync(first.Value, SignatureOnly(otherKey));
        Assert.True(valid.IsValid);
        Assert.False(wrongKey.IsValid);
        Assert.NotEqual(new JsonWebToken(first.Value).Id, new JsonWebToken(second.Value).Id);
    }

    [Fact]
    public async Task TheApiAcceptsAnIssuedTokenUntilThirtySecondsPastExpiryOnTheInjectedClock()
    {
        await using var factory = Fixture.CreateFactory();
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, UserRole.Administrator);
        var parameters = factory.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme)
            .TokenValidationParameters;
        var handler = new JsonWebTokenHandler();

        factory.Clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(30));
        var lastValidSecond = await handler.ValidateTokenAsync(client.AccessToken, parameters);
        using var accepted = await client.GetAsync("/api/checklist-templates");
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        var expired = await handler.ValidateTokenAsync(client.AccessToken, parameters);
        using var refused = await client.GetAsync("/api/checklist-templates");

        Assert.True(lastValidSecond.IsValid);
        Assert.NotEqual(HttpStatusCode.Unauthorized, accepted.StatusCode);
        Assert.False(expired.IsValid);
        using var problem = await ApiClient.AssertProblemAsync(refused, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        Assert.Equal("Bearer error=\"invalid_token\"", Assert.Single(refused.Headers.GetValues(HeaderNames.WWWAuthenticate)));
    }

    [Fact]
    public async Task AValidatedTokenBindsTheCurrentUserFromItsSubjectRoleAndSessionClaims()
    {
        var userId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();
        var token = TestTokens.Issue(Fixture.Factory, userId, UserRole.Technician, sessionId).Value;
        var parameters = Fixture.Factory.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme)
            .TokenValidationParameters;

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(result.ClaimsIdentity) };
        var bound = await CurrentUser.BindAsync(context);

        Assert.True(result.IsValid);
        Assert.Equal(new CurrentUser(userId, UserRole.Technician, sessionId), bound);
        Assert.True(context.User.IsInRole("Technician"));
        Assert.Equal(userId.ToString(), context.User.Identity!.Name);
    }

    [Theory]
    [InlineData(false, "sub", "role", "sid", "Administrator")]
    [InlineData(true, "nameid", "role", "sid", "Administrator")]
    [InlineData(true, "sub", "role", "sid", "1")]
    [InlineData(true, "sub", "role", "sid", "administrator")]
    [InlineData(true, "sub", "roles", "sid", "Administrator")]
    [InlineData(true, "sub", "role", "session", "Administrator")]
    public void APrincipalWithoutTheExactClaimsBindsNoCurrentUser(
        bool authenticated,
        string subjectType,
        string roleType,
        string sessionType,
        string role)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(subjectType, Guid.CreateVersion7().ToString()),
                new Claim(roleType, role),
                new Claim(sessionType, Guid.CreateVersion7().ToString()),
            ],
            authenticated ? "Bearer" : null);

        Assert.Null(CurrentUser.From(new ClaimsPrincipal(identity)));
    }

    [Fact]
    public async Task ARequestWithoutATokenGetsTheBearerChallengeWithoutAnErrorDescription()
    {
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.GetAsync("/api/checklist-templates");

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        Assert.Equal("Bearer", Assert.Single(response.Headers.GetValues(HeaderNames.WWWAuthenticate)));
    }

    [Fact]
    public async Task ATechnicianTokenOnAnAdministratorEndpointGets403Forbidden()
    {
        using var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TestTokens.For(Fixture.Factory, UserRole.Technician);

        using var response = await client.GetAsync("/api/checklist-templates");

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Forbidden, "auth.forbidden");
    }

    private static AccessTokenIssuer CreateIssuer(string key, TimeProvider time) =>
        new(Options.Create(new AuthOptions { AccessTokenKey = key, SocketTicketKey = RandomKey() }), time);

    private static TokenValidationParameters SignatureOnly(string key) => new()
    {
        IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(key)),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = false,
    };

    private static string RandomKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
