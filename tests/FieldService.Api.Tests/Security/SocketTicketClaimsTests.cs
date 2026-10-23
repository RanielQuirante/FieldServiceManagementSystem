using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Contracts;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FieldService.Api.Tests.Security;

[Collection(DatabaseCollection.Name)]
public sealed class SocketTicketClaimsTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    public const string SocketTicketsPath = "/api/auth/socket-tickets";

    public const string SignedOut = "signed out";

    public const string RefreshTokenReused = "refresh token reused";

    public const string OperatorRevoked = "revoked by an operator";

    public const string IdleExpired = "past its idle expiry";

    public const string AbsoluteExpired = "past its absolute expiry";

    public const string UserDeactivated = "its user deactivated";

    public static TheoryData<string> EndedSessions => new()
    {
        SignedOut,
        RefreshTokenReused,
        OperatorRevoked,
        IdleExpired,
        AbsoluteExpired,
        UserDeactivated,
    };

    [Fact]
    public async Task TheTicketVerifiesUnderTheSocketTicketKeyByAnIndependentHmacAndNeverUnderTheAccessTokenKey()
    {
        await using var factory = Fixture.CreateFactory();
        using var signedIn = await SignInAsync(factory);
        var socketKey = KeyOf(factory, AuthOptions.SocketTicketKeyName);
        var accessKey = KeyOf(factory, AuthOptions.AccessTokenKeyName);

        var ticket = await IssueAsync(signedIn.Client);

        var segments = ticket.Value.Split('.');
        var signedPart = Encoding.ASCII.GetBytes($"{segments[0]}.{segments[1]}");
        Assert.Equal(segments[2], Base64UrlEncoder.Encode(HMACSHA256.HashData(socketKey, signedPart)));
        Assert.NotEqual(segments[2], Base64UrlEncoder.Encode(HMACSHA256.HashData(accessKey, signedPart)));
        Assert.True((await SocketTicketVectorTests.ValidateAsync(ticket.Value, socketKey, factory.Clock.GetUtcNow())).IsValid);
        Assert.False((await SocketTicketVectorTests.ValidateAsync(ticket.Value, accessKey, factory.Clock.GetUtcNow())).IsValid);
        Assert.NotEqual(socketKey, accessKey);
    }

    [Fact]
    public async Task TheTicketCarriesExactlyTheClaimsOfR35InOrderAndTheResponseExpiryIsItsExp()
    {
        await using var factory = Fixture.CreateFactory();
        using var signedIn = await SignInAsync(factory);
        var issuedAt = factory.Clock.GetUtcNow().ToUnixTimeSeconds();

        var ticket = await IssueAsync(signedIn.Client);

        var segments = ticket.Value.Split('.');
        Assert.Equal(3, segments.Length);
        Assert.Equal(SocketTicketVectorTests.ContractHeader, Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(segments[0])));
        using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(segments[1]));
        var claims = payload.RootElement;
        Assert.Equal(SocketTicketVectorTests.ClaimOrder, claims.EnumerateObject().Select(claim => claim.Name));
        Assert.Equal("fieldservice-api", claims.GetProperty("iss").GetString());
        Assert.Equal("fieldservice-notifications", claims.GetProperty("aud").GetString());
        Assert.Equal(signedIn.User.Id.ToString(), claims.GetProperty("sub").GetString());
        Assert.Equal("Administrator", claims.GetProperty("role").GetString());
        Assert.Equal(7, Guid.Parse(claims.GetProperty("jti").GetString()!).Version);
        Assert.Equal(issuedAt, claims.GetProperty("iat").GetInt64());
        Assert.Equal(issuedAt, claims.GetProperty("nbf").GetInt64());
        Assert.Equal(issuedAt + 60, claims.GetProperty("exp").GetInt64());
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(issuedAt + 60), ticket.ExpiresAt);
        Assert.DoesNotContain(signedIn.User.Email, ticket.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(signedIn.User.DisplayName, ticket.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EachCallGetsANewUuidV7TicketIdStampedWithTheIssueInstant()
    {
        await using var factory = Fixture.CreateFactory();
        using var signedIn = await SignInAsync(factory);

        var tickets = new List<(DateTimeOffset At, Guid Id)>();
        for (var call = 0; call < 3; call++)
        {
            factory.Clock.Advance(TimeSpan.FromMilliseconds(1500));
            var at = factory.Clock.GetUtcNow();
            var ticket = await IssueAsync(signedIn.Client);
            tickets.Add((at, Guid.Parse(new JsonWebToken(ticket.Value).Id)));
        }

        Assert.Equal(3, tickets.Select(ticket => ticket.Id).Distinct().Count());
        Assert.All(tickets, ticket =>
        {
            Assert.Equal(7, ticket.Id.Version);
            Assert.Equal(ticket.At.ToUnixTimeMilliseconds(), EventFixtureTests.TimeOf(ticket.Id).ToUnixTimeMilliseconds());
        });
    }

    [Fact]
    public async Task TheAccessTokenIsNeverAValidTicketAndATicketIsNeverAValidAccessToken()
    {
        await using var factory = Fixture.CreateFactory();
        using var signedIn = await SignInAsync(factory);
        var ticket = await IssueAsync(signedIn.Client);
        using var ticketAsBearer = factory.CreateApiClient();
        ticketAsBearer.AccessToken = ticket.Value;

        var accessTokenAsTicket = await SocketTicketVectorTests.ValidateAsync(
            signedIn.Session.AccessToken,
            KeyOf(factory, AuthOptions.SocketTicketKeyName),
            factory.Clock.GetUtcNow());
        using var jobs = await ticketAsBearer.GetAsync("/api/jobs");
        using var anotherTicket = await ticketAsBearer.PostAsync(SocketTicketsPath);

        Assert.False(accessTokenAsTicket.IsValid);
        using var jobsProblem = await ApiClient.AssertProblemAsync(jobs, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        using var ticketProblem = await ApiClient.AssertProblemAsync(anotherTicket, HttpStatusCode.Unauthorized, "auth.unauthenticated");
    }

    [Theory]
    [MemberData(nameof(EndedSessions))]
    public async Task AStillValidAccessTokenGets401SessionExpiredOnceItsSessionHasEnded(string ending)
    {
        await using var factory = Fixture.CreateFactory();
        using var signedIn = await SignInAsync(factory);
        var live = await IssueAsync(signedIn.Client);
        Assert.False(string.IsNullOrEmpty(live.Value));

        await EndSessionAsync(factory, signedIn, ending);
        var issuedBefore = factory.LogSink.Named(SecurityLog.SocketTicketIssuedEvent).Count;
        using var response = await signedIn.Client.PostAsync(SocketTicketsPath);

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.session-expired");
        Assert.False(problem.RootElement.TryGetProperty("ticket", out _), "A refused request returned a ticket.");
        Assert.Empty(ApiClient.SetCookieHeadersOf(response));
        Assert.Equal(issuedBefore, factory.LogSink.Named(SecurityLog.SocketTicketIssuedEvent).Count);
    }

    private async Task EndSessionAsync(ApiFactory factory, TestData.SignedInSession signedIn, string ending)
    {
        var sessionId = SessionIdOf(signedIn);
        switch (ending)
        {
            case SignedOut:
                using (var signOut = await signedIn.SignOutAsync())
                {
                    Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
                }

                break;
            case RefreshTokenReused:
                using (var rotated = await signedIn.RefreshAsync())
                {
                    Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
                }

                factory.Clock.Advance(TimeSpan.FromSeconds(31));
                using (var reused = await signedIn.RefreshAsync())
                {
                    Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
                }

                Assert.Equal(SessionRevocationReason.RefreshTokenReuse, (await SessionAsync(sessionId)).RevokedReason);
                break;
            case OperatorRevoked:
                await using (var db = Fixture.CreateMigratorDbContext())
                {
                    await db.UserSessions
                        .Where(session => session.Id == sessionId)
                        .ExecuteUpdateAsync(
                            setters => setters
                                .SetProperty(session => session.RevokedAt, factory.Clock.GetUtcNow())
                                .SetProperty(session => session.RevokedReason, SessionRevocationReason.Operator),
                            CancellationToken);
                }

                break;
            case IdleExpired:
                await MoveToExpiryAsync(factory, signedIn, (await SessionAsync(sessionId)).IdleExpiresAt);
                break;
            case AbsoluteExpired:
                var cookie = signedIn.RefreshToken;
                var absolute = (await SessionAsync(sessionId)).AbsoluteExpiresAt;
                while (factory.Clock.GetUtcNow() + TimeSpan.FromMinutes(59) < absolute)
                {
                    factory.Clock.Advance(TimeSpan.FromMinutes(59));
                    using var refreshed = await signedIn.RefreshAsync(cookie);
                    Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
                    cookie = await signedIn.NextTokenAsync(refreshed);
                }

                var capped = await SessionAsync(sessionId);
                Assert.Equal(capped.AbsoluteExpiresAt, capped.IdleExpiresAt);
                await MoveToExpiryAsync(factory, signedIn, capped.AbsoluteExpiresAt);
                break;
            case UserDeactivated:
                await using (var db = Fixture.CreateMigratorDbContext())
                {
                    await db.Users
                        .Where(user => user.Id == signedIn.User.Id)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.IsActive, false), CancellationToken);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(ending), ending, "Unknown session ending.");
        }
    }

    private async Task MoveToExpiryAsync(ApiFactory factory, TestData.SignedInSession signedIn, DateTimeOffset expiry)
    {
        var sessionId = SessionIdOf(signedIn);
        factory.Clock.SetUtcNow(expiry.AddSeconds(-1));
        signedIn.Client.AccessToken = TestTokens.Issue(factory, signedIn.User.Id, UserRole.Administrator, sessionId).Value;
        var justBefore = await IssueAsync(signedIn.Client);
        Assert.False(string.IsNullOrEmpty(justBefore.Value));
        factory.Clock.SetUtcNow(expiry);
        signedIn.Client.AccessToken = TestTokens.Issue(factory, signedIn.User.Id, UserRole.Administrator, sessionId).Value;
    }

    private async Task<TestData.SignedInSession> SignInAsync(ApiFactory factory)
    {
        var signedIn = await new TestData(Fixture, factory.Clock.GetUtcNow()).SignInAsync(factory, SessionClient.Web, CancellationToken);
        signedIn.Client.AccessToken = signedIn.Session.AccessToken;
        return signedIn;
    }

    private async Task<UserSession> SessionAsync(Guid sessionId)
    {
        await using var db = Fixture.CreateAppDbContext();
        return await db.UserSessions.AsNoTracking().SingleAsync(session => session.Id == sessionId, CancellationToken);
    }

    private static async Task<IssuedTicket> IssueAsync(ApiClient client)
    {
        using var response = await client.PostAsync(SocketTicketsPath);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"The ticket request answered {(int)response.StatusCode}.");
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(["ticket", "expiresAt"], root.EnumerateObject().Select(member => member.Name));
        return new IssuedTicket(
            root.GetProperty("ticket").GetString()!,
            root.GetProperty("expiresAt").GetDateTimeOffset(),
            body);
    }

    private static byte[] KeyOf(ApiFactory factory, string name) => Convert.FromBase64String(factory.Settings[name]!);

    private static Guid SessionIdOf(TestData.SignedInSession signedIn) =>
        Guid.Parse(new JsonWebToken(signedIn.Session.AccessToken).GetClaim(AccessTokenClaims.SessionId).Value);

    private sealed record IssuedTicket(string Value, DateTimeOffset ExpiresAt, string Body)
    {
        public override string ToString() => $"ticket expiring at {ExpiresAt:O}";
    }
}
