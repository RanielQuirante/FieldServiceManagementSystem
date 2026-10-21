using System.Net;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Serilog.Events;

namespace FieldService.Api.Tests.Features.Auth;

[Collection(DatabaseCollection.Name)]
public sealed class RefreshTokenReuseTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    [Theory]
    [InlineData(SessionClient.Web)]
    [InlineData(SessionClient.Mobile)]
    public async Task ATokenPresentedAgainWithinThirtySecondsWhileItsSuccessorIsUnusedGetsAFreshSuccessor(SessionClient sessionClient)
    {
        await using var factory = CreateFactory();
        using var signedIn = await SignInAsync(factory, sessionClient);
        using var first = await signedIn.RefreshAsync();
        var lostSuccessor = await signedIn.NextTokenAsync(first);
        factory.Clock.Advance(TimeSpan.FromSeconds(29));

        using var retry = await signedIn.RefreshAsync();

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var freshSuccessor = await signedIn.NextTokenAsync(retry);
        Assert.NotEqual(lostSuccessor, freshSuccessor);
        await using (var db = Fixture.CreateAppDbContext())
        {
            var presented = await TokenAsync(db, signedIn.RefreshToken);
            var revoked = await TokenAsync(db, lostSuccessor);
            var fresh = await TokenAsync(db, freshSuccessor);
            Assert.Equal(fresh.Id, presented.ReplacedById);
            Assert.Equal(factory.Clock.GetUtcNow(), revoked.ConsumedAt);
            Assert.Null(revoked.ReplacedById);
            Assert.Null(fresh.ConsumedAt);
            Assert.Equal(1, await db.RefreshTokens.CountAsync(token => token.ConsumedAt == null, CancellationToken));
            Assert.Null((await db.UserSessions.AsNoTracking().SingleAsync(CancellationToken)).RevokedAt);
        }

        var grace = Assert.Single(factory.LogSink.Named(SecurityLog.GraceRetryEvent));
        Assert.Equal(LogEventLevel.Information, grace.Level);
        Assert.Equal(signedIn.User.Id.ToString(), CapturingLogSink.Scalar(grace, "UserId"));
        Assert.Equal(sessionClient.ToString(), CapturingLogSink.Scalar(grace, "Client"));
        using var continued = await signedIn.RefreshAsync(freshSuccessor);
        Assert.Equal(HttpStatusCode.OK, continued.StatusCode);
    }

    [Fact]
    public async Task ATokenPresentedThirtySecondsAfterItsRotationRevokesTheSessionAsReuse()
    {
        await using var factory = CreateFactory();
        using var signedIn = await SignInAsync(factory, SessionClient.Mobile);
        using var first = await signedIn.RefreshAsync();
        var successor = await signedIn.NextTokenAsync(first);
        factory.Clock.Advance(TimeSpan.FromSeconds(30));

        using var reused = await signedIn.RefreshAsync();
        using var successorAfterwards = await signedIn.RefreshAsync(successor);

        using var problem = await ApiClient.AssertProblemAsync(reused, HttpStatusCode.Unauthorized, "auth.session-expired");
        using var after = await ApiClient.AssertProblemAsync(successorAfterwards, HttpStatusCode.Unauthorized, "auth.session-expired");
        await AssertRevokedForReuseAsync();
        var warning = Assert.Single(factory.LogSink.Named(SecurityLog.ReuseDetectedEvent));
        Assert.Equal(LogEventLevel.Warning, warning.Level);
        Assert.Equal(signedIn.User.Id.ToString(), CapturingLogSink.Scalar(warning, "UserId"));
        Assert.Equal("Mobile", CapturingLogSink.Scalar(warning, "Client"));
        Assert.Equal("30", CapturingLogSink.Scalar(warning, "SecondsSinceConsumed"));
        Assert.Equal(signedIn.Client.RemoteAddress.ToString(), CapturingLogSink.Scalar(warning, "ClientIp"));
        Assert.False(string.IsNullOrEmpty(CapturingLogSink.Scalar(warning, "SessionId")));
        foreach (var logEvent in factory.LogSink.Events)
        {
            var text = CapturingLogSink.Text(logEvent);
            Assert.DoesNotContain(signedIn.RefreshToken, text, StringComparison.Ordinal);
            Assert.DoesNotContain(successor, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ATokenPresentedAfterItsSuccessorWasUsedRevokesTheSessionEvenWithinThirtySeconds()
    {
        await using var factory = CreateFactory();
        using var signedIn = await SignInAsync(factory, SessionClient.Web);
        using var first = await signedIn.RefreshAsync();
        var successor = await signedIn.NextTokenAsync(first);
        using var second = await signedIn.RefreshAsync(successor);
        var latest = await signedIn.NextTokenAsync(second);
        factory.Clock.Advance(TimeSpan.FromSeconds(5));

        using var reused = await signedIn.RefreshAsync();
        using var latestAfterwards = await signedIn.RefreshAsync(latest);

        using var problem = await ApiClient.AssertProblemAsync(reused, HttpStatusCode.Unauthorized, "auth.session-expired");
        ApiClient.AssertRefreshCookieCleared(reused);
        using var after = await ApiClient.AssertProblemAsync(latestAfterwards, HttpStatusCode.Unauthorized, "auth.session-expired");
        await AssertRevokedForReuseAsync();
    }

    [Fact]
    public async Task ASuccessorRevokedByAGraceRetryIsNeverEligibleAgain()
    {
        await using var factory = CreateFactory();
        using var signedIn = await SignInAsync(factory, SessionClient.Mobile);
        using var first = await signedIn.RefreshAsync();
        var lostSuccessor = await signedIn.NextTokenAsync(first);
        using var retry = await signedIn.RefreshAsync();
        var freshSuccessor = await signedIn.NextTokenAsync(retry);

        using var revokedAgain = await signedIn.RefreshAsync(lostSuccessor);
        using var freshAfterwards = await signedIn.RefreshAsync(freshSuccessor);

        using var problem = await ApiClient.AssertProblemAsync(revokedAgain, HttpStatusCode.Unauthorized, "auth.session-expired");
        using var after = await ApiClient.AssertProblemAsync(freshAfterwards, HttpStatusCode.Unauthorized, "auth.session-expired");
        await AssertRevokedForReuseAsync();
        Assert.Single(factory.LogSink.Named(SecurityLog.ReuseDetectedEvent));
    }

    [Fact]
    public async Task TwoParallelRefreshesWithOneTokenAreSerializedAndLeaveExactlyOneLiveSuccessor()
    {
        await using var factory = CreateFactory();
        using var signedIn = await SignInAsync(factory, SessionClient.Mobile);

        var responses = await Task.WhenAll(signedIn.RefreshAsync(), signedIn.RefreshAsync());

        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            var issued = new[] { await signedIn.NextTokenAsync(responses[0]), await signedIn.NextTokenAsync(responses[1]) };
            Assert.NotEqual(issued[0], issued[1]);
            await using var db = Fixture.CreateAppDbContext();
            var live = await db.RefreshTokens.AsNoTracking().Where(token => token.ConsumedAt == null).ToListAsync(CancellationToken);
            var survivor = Assert.Single(live);
            var survivorToken = Assert.Single(issued, token => ApiClient.RefreshTokenHashOf(token).AsSpan().SequenceEqual(survivor.TokenHash));
            Assert.Single(factory.LogSink.Named(SecurityLog.GraceRetryEvent));
            using var continued = await signedIn.RefreshAsync(survivorToken);
            Assert.Equal(HttpStatusCode.OK, continued.StatusCode);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    private ApiFactory CreateFactory() =>
        Fixture.CreateFactory(factory => factory.Settings["Serilog:MinimumLevel:Default"] = "Information");

    private Task<TestData.SignedInSession> SignInAsync(ApiFactory factory, SessionClient sessionClient) =>
        new TestData(Fixture, factory.Clock.GetUtcNow()).SignInAsync(factory, sessionClient, CancellationToken);

    private static Task<RefreshToken> TokenAsync(Api.Persistence.FieldServiceDbContext db, string token)
    {
        var hash = ApiClient.RefreshTokenHashOf(token);
        return db.RefreshTokens.AsNoTracking().SingleAsync(entry => entry.TokenHash == hash, TestContext.Current.CancellationToken);
    }

    private async Task AssertRevokedForReuseAsync()
    {
        await using var db = Fixture.CreateAppDbContext();
        var session = await db.UserSessions.AsNoTracking().SingleAsync(CancellationToken);
        Assert.Equal(SessionRevocationReason.RefreshTokenReuse, session.RevokedReason);
        Assert.NotNull(session.RevokedAt);
    }
}
