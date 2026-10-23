using System.Net;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Http;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Serilog.Events;

namespace FieldService.Api.Tests.Features.Auth;

[Collection(DatabaseCollection.Name)]
public sealed class IssueSocketTicketTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    public const string Path = "/api/auth/socket-tickets";

    private static readonly string[] _claimOrder = ["iss", "aud", "sub", "role", "jti", "iat", "nbf", "exp"];

    [Fact]
    public async Task AnAdministratorWithALiveSessionGetsASixtySecondTicketSignedWithTheSocketTicketKey()
    {
        await using var factory = CreateFactory();
        using var signedIn = await SignInAsync(factory, SessionClient.Web);
        var issuedAt = factory.Clock.GetUtcNow();

        using var response = await signedIn.Client.PostAsync(Path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(["ticket", "expiresAt"], body.RootElement.EnumerateObject().Select(member => member.Name));
        Assert.Equal(ApiClient.WireInstant(issuedAt.AddSeconds(60)), body.RootElement.GetProperty("expiresAt").GetString());
        var ticket = body.RootElement.GetProperty("ticket").GetString()!;
        var jwt = new JsonWebToken(ticket);
        Assert.Equal("HS256", jwt.Alg);
        Assert.Equal("JWT", jwt.Typ);
        using var claims = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(ticket.Split('.')[1]));
        Assert.Equal(_claimOrder, claims.RootElement.EnumerateObject().Select(claim => claim.Name));
        Assert.Equal("fieldservice-api", jwt.Issuer);
        Assert.Equal(["fieldservice-notifications"], jwt.Audiences);
        Assert.Equal(signedIn.User.Id.ToString(), jwt.Subject);
        Assert.Equal("Administrator", jwt.GetClaim("role").Value);
        Assert.Equal(7, Guid.Parse(jwt.Id).Version);
        Assert.Equal(issuedAt.ToUnixTimeSeconds(), claims.RootElement.GetProperty("iat").GetInt64());
        Assert.Equal(issuedAt.ToUnixTimeSeconds(), claims.RootElement.GetProperty("nbf").GetInt64());
        Assert.Equal(issuedAt.ToUnixTimeSeconds() + 60, claims.RootElement.GetProperty("exp").GetInt64());
        Assert.True((await ValidateAsync(ticket, KeyOf(factory, AuthOptions.SocketTicketKeyName), issuedAt)).IsValid);
        Assert.False((await ValidateAsync(ticket, KeyOf(factory, AuthOptions.AccessTokenKeyName), issuedAt)).IsValid);
    }

    [Fact]
    public async Task EachCallIssuesANewTicketWithANewTicketIdAndLogsTheUserAndTicketIdButNeverTheTicket()
    {
        await using var factory = CreateFactory();
        using var signedIn = await SignInAsync(factory, SessionClient.Web);

        var tickets = new List<string>();
        for (var call = 0; call < 3; call++)
        {
            using var response = await signedIn.Client.PostAsync(Path);
            tickets.Add(await TicketOfAsync(response));
        }

        var ids = tickets.Select(ticket => new JsonWebToken(ticket).Id).ToList();
        Assert.Equal(3, ids.Distinct(StringComparer.Ordinal).Count());
        var issued = factory.LogSink.Named(SecurityLog.SocketTicketIssuedEvent);
        Assert.Equal(3, issued.Count);
        Assert.All(issued, entry =>
        {
            Assert.Equal(LogEventLevel.Information, entry.Level);
            Assert.Equal(signedIn.User.Id.ToString(), CapturingLogSink.Scalar(entry, "UserId"));
        });
        Assert.Equal(ids, issued.Select(entry => CapturingLogSink.Scalar(entry, "TicketId")));
        var secrets = tickets
            .SelectMany(ticket => new[] { ticket, ticket[(ticket.LastIndexOf('.') + 1)..] })
            .Append(signedIn.Session.AccessToken)
            .ToList();
        Assert.DoesNotContain(
            factory.LogSink.Events.Select(CapturingLogSink.Text),
            text => secrets.Any(secret => text.Contains(secret, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ATechnicianGets403AndAnAnonymousCallerOrAForgedTokenGets401()
    {
        using var technician = await SignInAsync(Fixture.Factory, SessionClient.Mobile);
        using var anonymous = Fixture.Factory.CreateApiClient();
        using var forged = Fixture.Factory.CreateApiClient();
        forged.AccessToken = "not-a-token";

        using var technicianResponse = await technician.Client.PostAsync(Path);
        using var anonymousResponse = await anonymous.PostAsync(Path);
        using var forgedResponse = await forged.PostAsync(Path);

        using var forbidden = await ApiClient.AssertProblemAsync(technicianResponse, HttpStatusCode.Forbidden, "auth.forbidden");
        using var unauthenticated = await ApiClient.AssertProblemAsync(anonymousResponse, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        using var invalid = await ApiClient.AssertProblemAsync(forgedResponse, HttpStatusCode.Unauthorized, "auth.unauthenticated");
    }

    [Fact]
    public async Task AfterSignOutTheStillValidAccessTokenGets401SessionExpired()
    {
        using var signedIn = await SignInAsync(Fixture.Factory, SessionClient.Web);
        using var before = await signedIn.Client.PostAsync(Path);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using var signOut = await signedIn.SignOutAsync();
        using var after = await signedIn.Client.PostAsync(Path);

        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        using var problem = await ApiClient.AssertProblemAsync(after, HttpStatusCode.Unauthorized, "auth.session-expired");
        Assert.Empty(ApiClient.SetCookieHeadersOf(after));
    }

    [Fact]
    public async Task AfterRefreshTokenReuseRevokesTheSessionTheStillValidAccessTokenGets401SessionExpired()
    {
        await using var factory = CreateFactory();
        using var signedIn = await SignInAsync(factory, SessionClient.Web);
        using var rotated = await signedIn.RefreshAsync();
        factory.Clock.Advance(TimeSpan.FromSeconds(31));
        using var reused = await signedIn.RefreshAsync();

        using var response = await signedIn.Client.PostAsync(Path);

        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        Assert.Equal(SessionRevocationReason.RefreshTokenReuse, (await SessionOfAsync(signedIn)).RevokedReason);
        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.session-expired");
    }

    [Fact]
    public async Task AnOperatorRevocationGets401SessionExpired()
    {
        using var signedIn = await SignInAsync(Fixture.Factory, SessionClient.Web);
        var sessionId = SessionIdOf(signedIn);
        await using (var db = Fixture.CreateMigratorDbContext())
        {
            await db.UserSessions
                .Where(session => session.Id == sessionId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(session => session.RevokedAt, Fixture.Factory.Clock.GetUtcNow())
                        .SetProperty(session => session.RevokedReason, SessionRevocationReason.Operator),
                    CancellationToken);
        }

        using var response = await signedIn.Client.PostAsync(Path);

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.session-expired");
    }

    [Fact]
    public async Task ASessionIsLiveUntilItsIdleExpiryAndAFreshAccessTokenForItGets401FromThatInstant()
    {
        await using var factory = CreateFactory();
        using var signedIn = await SignInAsync(factory, SessionClient.Web);
        var session = await SessionOfAsync(signedIn);
        using var client = factory.CreateApiClient();

        factory.Clock.SetUtcNow(session.IdleExpiresAt.AddSeconds(-1));
        client.AccessToken = TestTokens.Issue(factory, signedIn.User.Id, UserRole.Administrator, session.Id).Value;
        using var justBefore = await client.PostAsync(Path);
        factory.Clock.SetUtcNow(session.IdleExpiresAt);
        client.AccessToken = TestTokens.Issue(factory, signedIn.User.Id, UserRole.Administrator, session.Id).Value;
        using var atExpiry = await client.PostAsync(Path);

        Assert.Equal(HttpStatusCode.OK, justBefore.StatusCode);
        using var problem = await ApiClient.AssertProblemAsync(atExpiry, HttpStatusCode.Unauthorized, "auth.session-expired");
    }

    [Fact]
    public async Task ASessionRefreshedUpToItsAbsoluteExpiryIsLiveUntilThatInstantAndGets401FromIt()
    {
        await using var factory = CreateFactory();
        using var signedIn = await SignInAsync(factory, SessionClient.Web);
        var created = await SessionOfAsync(signedIn);
        var cookie = signedIn.RefreshToken;
        while (factory.Clock.GetUtcNow() + TimeSpan.FromMinutes(59) < created.AbsoluteExpiresAt)
        {
            factory.Clock.Advance(TimeSpan.FromMinutes(59));
            using var refreshed = await signedIn.RefreshAsync(cookie);
            Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
            cookie = await signedIn.NextTokenAsync(refreshed);
        }

        var capped = await SessionOfAsync(signedIn);
        using var client = factory.CreateApiClient();

        factory.Clock.SetUtcNow(capped.AbsoluteExpiresAt.AddSeconds(-1));
        client.AccessToken = TestTokens.Issue(factory, signedIn.User.Id, UserRole.Administrator, capped.Id).Value;
        using var justBefore = await client.PostAsync(Path);
        factory.Clock.SetUtcNow(capped.AbsoluteExpiresAt);
        client.AccessToken = TestTokens.Issue(factory, signedIn.User.Id, UserRole.Administrator, capped.Id).Value;
        using var atExpiry = await client.PostAsync(Path);

        Assert.Equal(capped.AbsoluteExpiresAt, capped.IdleExpiresAt);
        Assert.Equal(created.AbsoluteExpiresAt, capped.AbsoluteExpiresAt);
        Assert.Equal(HttpStatusCode.OK, justBefore.StatusCode);
        using var problem = await ApiClient.AssertProblemAsync(atExpiry, HttpStatusCode.Unauthorized, "auth.session-expired");
    }

    [Fact]
    public async Task AnInactiveUserGets401SessionExpired()
    {
        using var signedIn = await SignInAsync(Fixture.Factory, SessionClient.Web);
        await using (var db = Fixture.CreateMigratorDbContext())
        {
            await db.Users
                .Where(user => user.Id == signedIn.User.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.IsActive, false), CancellationToken);
        }

        using var response = await signedIn.Client.PostAsync(Path);

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.session-expired");
    }

    [Fact]
    public async Task ATokenWhoseSessionDoesNotExistOrBelongsToAnotherUserGets401SessionExpired()
    {
        using var signedIn = await SignInAsync(Fixture.Factory, SessionClient.Web);
        var other = await Data(Fixture.Factory).CreateAdministratorAsync(CancellationToken);
        using var unknownSession = Fixture.Factory.CreateApiClient();
        unknownSession.AccessToken = TestTokens.For(Fixture.Factory, signedIn.User);
        using var borrowedSession = Fixture.Factory.CreateApiClient();
        borrowedSession.AccessToken = TestTokens.For(Fixture.Factory, other, SessionIdOf(signedIn));

        using var unknownResponse = await unknownSession.PostAsync(Path);
        using var borrowedResponse = await borrowedSession.PostAsync(Path);

        using var unknown = await ApiClient.AssertProblemAsync(unknownResponse, HttpStatusCode.Unauthorized, "auth.session-expired");
        using var borrowed = await ApiClient.AssertProblemAsync(borrowedResponse, HttpStatusCode.Unauthorized, "auth.session-expired");
    }

    [Fact]
    public async Task TheThirtyFirstTicketRequestOfOneUserInAMinuteGets429AndIsLoggedOnceForTheUserPartition()
    {
        await using var factory = CreateFactory();
        using var signedIn = await SignInAsync(factory, SessionClient.Web);

        var statuses = new List<HttpStatusCode>();
        for (var call = 0; call < 31; call++)
        {
            using var response = await signedIn.Client.PostAsync(Path);
            statuses.Add(response.StatusCode);
        }

        using var rejected = await signedIn.Client.PostAsync(Path);

        Assert.All(statuses.Take(30), status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[30]);
        using var problem = await ApiClient.AssertProblemAsync(rejected, HttpStatusCode.TooManyRequests, "rate-limit.exceeded");
        Assert.NotNull(rejected.Headers.RetryAfter);
        var logged = Assert.Single(factory.LogSink.Named(SecurityLog.RateLimitRejectedEvent));
        Assert.Equal(RateLimitPolicies.SocketTickets, CapturingLogSink.Scalar(logged, "Policy"));
        Assert.Equal(RateLimitPolicies.UserPartitionKind, CapturingLogSink.Scalar(logged, "PartitionKind"));
        Assert.Equal(signedIn.User.Id.ToString(), CapturingLogSink.Scalar(logged, "UserId"));
        Assert.True(string.IsNullOrEmpty(CapturingLogSink.Scalar(logged, "ClientIp")));
        Assert.Equal(30, factory.LogSink.Named(SecurityLog.SocketTicketIssuedEvent).Count);
    }

    [Fact]
    public async Task ATicketSentAsABearerTokenToTheApiGets401()
    {
        using var signedIn = await SignInAsync(Fixture.Factory, SessionClient.Web);
        using var response = await signedIn.Client.PostAsync(Path);
        using var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = await TicketOfAsync(response);

        using var jobs = await client.GetAsync("/api/jobs");
        using var ticketAgain = await client.PostAsync(Path);

        using var jobsProblem = await ApiClient.AssertProblemAsync(jobs, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        using var ticketProblem = await ApiClient.AssertProblemAsync(ticketAgain, HttpStatusCode.Unauthorized, "auth.unauthenticated");
    }

    [Fact]
    public async Task TheSessionCheckIsOneStatementOnTheSessionAndUserPrimaryKeys()
    {
        await using var factory = Fixture.CreateFactory(factory =>
        {
            factory.Settings["Serilog:MinimumLevel:Default"] = "Information";
            factory.Settings["Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore"] = "Information";
        });
        using var signedIn = await SignInAsync(factory, SessionClient.Web);
        factory.LogSink.Clear();

        using var response = await signedIn.Client.PostAsync(Path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var commands = factory.LogSink.Events
            .Where(entry => CapturingLogSink.Scalar(entry, "SourceContext") == "Microsoft.EntityFrameworkCore.Database.Command"
                && CapturingLogSink.Scalar(entry, "commandText") is not null)
            .Select(entry => CapturingLogSink.Scalar(entry, "commandText")!)
            .ToList();
        var command = Assert.Single(commands);
        Assert.StartsWith("SELECT EXISTS (", command, StringComparison.Ordinal);
        Assert.Contains("FROM fieldservice.user_sessions AS", command, StringComparison.Ordinal);
        Assert.Contains("fieldservice.users AS", command, StringComparison.Ordinal);
        Assert.Contains(".id = @", command, StringComparison.Ordinal);
    }

    private static async Task<string> TicketOfAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return body.RootElement.GetProperty("ticket").GetString()!;
    }

    private static byte[] KeyOf(ApiFactory factory, string name) => Convert.FromBase64String(factory.Settings[name]!);

    private static Guid SessionIdOf(TestData.SignedInSession signedIn) =>
        Guid.Parse(new JsonWebToken(signedIn.Session.AccessToken).GetClaim(AccessTokenClaims.SessionId).Value);

    private static Task<TokenValidationResult> ValidateAsync(string ticket, byte[] key, DateTimeOffset at) =>
        new JsonWebTokenHandler().ValidateTokenAsync(ticket, new TokenValidationParameters
        {
            ValidIssuer = "fieldservice-api",
            ValidAudience = "fieldservice-notifications",
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            IssuerSigningKey = new SymmetricSecurityKey(key),
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            LifetimeValidator = (notBefore, expires, _, _) => notBefore <= at.UtcDateTime && expires > at.UtcDateTime,
        });

    private ApiFactory CreateFactory() =>
        Fixture.CreateFactory(factory => factory.Settings["Serilog:MinimumLevel:Default"] = "Information");

    private TestData Data(ApiFactory factory) => new(Fixture, factory.Clock.GetUtcNow());

    private async Task<TestData.SignedInSession> SignInAsync(ApiFactory factory, SessionClient sessionClient)
    {
        var signedIn = await Data(factory).SignInAsync(factory, sessionClient, CancellationToken);
        signedIn.Client.AccessToken = signedIn.Session.AccessToken;
        return signedIn;
    }

    private async Task<UserSession> SessionOfAsync(TestData.SignedInSession signedIn)
    {
        var sessionId = SessionIdOf(signedIn);
        await using var db = Fixture.CreateAppDbContext();
        return await db.UserSessions.AsNoTracking().SingleAsync(session => session.Id == sessionId, CancellationToken);
    }
}
