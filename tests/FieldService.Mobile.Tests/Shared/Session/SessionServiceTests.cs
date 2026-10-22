using System.Net;
using FieldService.Mobile.Features.SignIn;
using FieldService.Mobile.Shared.Navigation;
using FieldService.Mobile.Shared.Session;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace FieldService.Mobile.Tests.Shared.Session;

public sealed class SessionServiceTests
{
    private const string SignInPath = "/api/auth/sign-in";
    private const string RefreshPath = "/api/auth/refresh";
    private const string SignOutPath = "/api/auth/sign-out";

    private static readonly Guid _technicianId = Guid.Parse("01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e");
    private static readonly Guid _nextTechnicianId = Guid.Parse("01a0f6b1-2a68-77d9-8aa0-8a58bef6a9a0");

    [Fact]
    public async Task StartupHoldsTheSplashUntilMyJobsIsShownNotOnlyUntilTheNavigationReturns()
    {
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence(
            () => ProblemResponses.Session(TestSession.NewAccessToken(), TestSession.NewRefreshToken(), _technicianId, "Aisha Karim")));
        harness.StoreRefreshToken(TestSession.NewRefreshToken());
        var navigated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Navigator.GoToAsync(Routes.Jobs).Returns(navigated.Task);
        var startup = new AppStartup(harness.Session, harness.Navigator, new FakeTimeProvider(), harness.Logs.CreateLogger<AppStartup>());
        startup.ReportShown(Routes.SignIn);

        var running = startup.RunAsync(CancellationToken.None);
        await WaitFor(() => harness.Navigator.ReceivedCalls().Any());

        Assert.True(startup.HoldsSplash);
        Assert.False(startup.IsComplete);
        navigated.SetResult();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.True(startup.HoldsSplash);
        Assert.False(startup.IsComplete);
        Assert.False(running.IsCompleted);

        startup.ReportShown(Routes.Jobs);
        await running;

        Assert.True(startup.IsComplete);
        Assert.False(startup.HoldsSplash);
    }

    [Fact]
    public async Task WithoutAStoredSessionTheSplashHoldsUntilTheSignInScreenIsShown()
    {
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence());
        var startup = new AppStartup(harness.Session, harness.Navigator, new FakeTimeProvider(), harness.Logs.CreateLogger<AppStartup>());

        var running = startup.RunAsync(CancellationToken.None);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.True(startup.HoldsSplash);
        Assert.False(running.IsCompleted);

        startup.ReportShown(Routes.SignIn);
        await running;
        await startup.RunAsync(CancellationToken.None);

        Assert.True(startup.IsComplete);
        await harness.Navigator.DidNotReceive().GoToAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task TheSplashIsReleasedAtTheCapWhenNoScreenReportsItself()
    {
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence());
        var clock = new FakeTimeProvider();
        var startup = new AppStartup(harness.Session, harness.Navigator, clock, harness.Logs.CreateLogger<AppStartup>());

        var running = startup.RunAsync(CancellationToken.None);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        clock.Advance(AppStartup.MaximumHold);
        await running.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(startup.IsComplete);
        Assert.False(startup.HoldsSplash);
        Assert.Contains("app.startup-screen-not-shown //sign-in", harness.Logs.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MyJobsBeingShownReleasesARestoreThatWaitsForIt()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence());
        harness.Session.TryRestoreAsync(Arg.Any<CancellationToken>()).Returns(Routes.Jobs);
        var viewModel = harness.CreateMyJobs();

        var running = harness.Startup.RunAsync(CancellationToken.None);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(running.IsCompleted);
        Assert.True(harness.Startup.HoldsSplash);

        viewModel.LoadedCommand.Execute(null);
        await running.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(harness.Startup.IsComplete);
        await harness.Navigator.Received(1).GoToAsync(Routes.Jobs);
    }

    [Theory]
    [InlineData("Features/Jobs/MyJobsPage.xaml.cs")]
    [InlineData("Features/SignIn/SignInPage.xaml.cs")]
    public void TheFirstScreensReportThemselvesWhenTheirViewIsLoaded(string file)
    {
        var source = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, file));

        Assert.Contains("Loaded += (_, _) => _viewModel.LoadedCommand.Execute(null);", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AScreenThatReportsItselfBeforeTheRestoreEndsIsNotWaitedForAgain()
    {
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence(
            () => ProblemResponses.Session(TestSession.NewAccessToken(), TestSession.NewRefreshToken(), _technicianId, "Aisha Karim")));
        harness.StoreRefreshToken(TestSession.NewRefreshToken());
        var startup = new AppStartup(harness.Session, harness.Navigator, new FakeTimeProvider(), harness.Logs.CreateLogger<AppStartup>());
        harness.Navigator.GoToAsync(Routes.Jobs).Returns(_ =>
        {
            startup.ReportShown(Routes.Jobs);
            return Task.CompletedTask;
        });

        await startup.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(startup.IsComplete);
    }

    [Fact]
    public async Task StartupReleasesTheSplash_WhenTheRestoreFails()
    {
        var session = Substitute.For<ISessionService>();
        session.TryRestoreAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException());
        var logs = new CapturingLoggerProvider();
        var startup = new AppStartup(session, Substitute.For<INavigator>(), new FakeTimeProvider(), logs.CreateLogger<AppStartup>());

        await startup.RunAsync(CancellationToken.None);

        Assert.True(startup.IsComplete);
        Assert.Contains("app.startup-failed InvalidOperationException", logs.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheSplashIsNeverHeldLongerThanTheMaximumHold()
    {
        var clock = new FakeTimeProvider();
        var startup = new AppStartup(Substitute.For<ISessionService>(), Substitute.For<INavigator>(), clock, new CapturingLoggerProvider().CreateLogger<AppStartup>());

        Assert.True(startup.HoldsSplash);
        clock.Advance(AppStartup.MaximumHold);
        Assert.False(startup.HoldsSplash);
        Assert.False(startup.IsComplete);
    }

    [Fact]
    public async Task SignInKeepsTheTypedEmailInMemoryForTheAccountSheetAcrossRefreshes()
    {
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence(
            () => ProblemResponses.Session(TestSession.NewAccessToken(), TestSession.NewRefreshToken(), _technicianId, "Aisha Karim"),
            () => ProblemResponses.Session(TestSession.NewAccessToken(), TestSession.NewRefreshToken(), _technicianId, "Aisha Karim")));

        await harness.Session.SignInAsync("aisha.karim@fieldservice.example", TestSession.NewPassword(), CancellationToken.None);
        await harness.Session.RefreshAccessTokenAsync(harness.Session.AccessToken, CancellationToken.None);

        Assert.Equal("aisha.karim@fieldservice.example", harness.Session.User?.Email);
        Assert.DoesNotContain("aisha.karim", harness.Session.User!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(harness.Values.Values, value => value.Contains("aisha.karim", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestoreGoesToSignInWithoutARequest_WhenNoTokenIsStored()
    {
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence());

        var route = await harness.Session.TryRestoreAsync(CancellationToken.None);

        Assert.Equal(Routes.SignIn, route);
        Assert.Empty(harness.Stub.Requests);
        Assert.Null(harness.Session.AccessToken);
    }

    [Fact]
    public async Task RestoreGoesToJobsAndKeepsTheRotatedTokens_WhenTheRefreshSucceeds()
    {
        var storedToken = TestSession.NewRefreshToken();
        var rotatedToken = TestSession.NewRefreshToken();
        var accessToken = TestSession.NewAccessToken();
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence(
            () => ProblemResponses.Session(accessToken, rotatedToken, _technicianId, "Aisha Karim")));
        harness.StoreRefreshToken(storedToken);

        var route = await harness.Session.TryRestoreAsync(CancellationToken.None);

        Assert.Equal(Routes.Jobs, route);
        Assert.Equal(accessToken, harness.Session.AccessToken);
        Assert.Equal(new SignedInUser(_technicianId, "Aisha Karim", UserRole.Technician), harness.Session.User);
        Assert.Equal(rotatedToken, harness.StoredRefreshToken);
        var request = Assert.Single(harness.Stub.Requests);
        Assert.Equal("/api/auth/refresh", request.Path);
        Assert.Null(request.Authorization);
        Assert.Equal(storedToken, ProblemResponses.ReadBody(request).GetProperty("refreshToken").GetString());
    }

    [Fact]
    public async Task RestoreRemovesAllAndGoesToSignIn_WhenTheRefreshIsRejected()
    {
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence(ProblemResponses.SessionExpired));
        harness.StoreRefreshToken(TestSession.NewRefreshToken());

        var route = await harness.Session.TryRestoreAsync(CancellationToken.None);

        Assert.Equal(Routes.SignIn, route);
        harness.Storage.Received(1).RemoveAll();
        Assert.Null(harness.StoredRefreshToken);
        Assert.Null(harness.Session.AccessToken);
    }

    [Fact]
    public async Task RestoreRemovesAllAndGoesToSignIn_WhenSecureStorageCannotBeRead()
    {
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence());
        harness.Storage.GetAsync(TokenStore.RefreshTokenKey).ThrowsAsync(new InvalidOperationException("keystore entry unreadable"));

        var route = await harness.Session.TryRestoreAsync(CancellationToken.None);

        Assert.Equal(Routes.SignIn, route);
        harness.Storage.Received(1).RemoveAll();
        Assert.Empty(harness.Stub.Requests);
    }

    [Fact]
    public async Task RestoreKeepsTheTokenAndGoesToJobs_WhenTheApiCannotBeReached()
    {
        var storedToken = TestSession.NewRefreshToken();
        var harness = TestSession.Create(StubHttpMessageHandler.Throwing(new HttpRequestException("Connection refused")));
        harness.StoreRefreshToken(storedToken);

        var route = await harness.Session.TryRestoreAsync(CancellationToken.None);

        Assert.Equal(Routes.Jobs, route);
        Assert.Equal(storedToken, harness.StoredRefreshToken);
        harness.Storage.DidNotReceive().RemoveAll();
        Assert.Null(harness.Session.AccessToken);
    }

    [Fact]
    public async Task RestoreKeepsTheTokenAndGoesToJobs_WhenTheServerFails()
    {
        var storedToken = TestSession.NewRefreshToken();
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence(() => ProblemResponses.ServerError(Guid.NewGuid().ToString("D"))));
        harness.StoreRefreshToken(storedToken);

        var route = await harness.Session.TryRestoreAsync(CancellationToken.None);

        Assert.Equal(Routes.Jobs, route);
        Assert.Equal(storedToken, harness.StoredRefreshToken);
    }

    [Fact]
    public async Task SignInStoresTheRefreshTokenAndKeepsTheAccessTokenInMemory()
    {
        var refreshToken = TestSession.NewRefreshToken();
        var accessToken = TestSession.NewAccessToken();
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence(
            () => ProblemResponses.Session(accessToken, refreshToken, _technicianId, "Aisha Karim")));

        var user = await harness.Session.SignInAsync("aisha.karim@fieldservice.example", TestSession.NewPassword(), CancellationToken.None);

        Assert.Equal(_technicianId, user.Id);
        Assert.Equal(accessToken, harness.Session.AccessToken);
        await harness.Storage.Received(1).SetAsync(TokenStore.RefreshTokenKey, refreshToken);
        await harness.Storage.DidNotReceive().SetAsync(Arg.Any<string>(), accessToken);
        var body = ProblemResponses.ReadBody(Assert.Single(harness.Stub.Requests));
        Assert.Equal("Mobile", body.GetProperty("client").GetString());
    }

    [Fact]
    public async Task SignOutCallsTheApiThenClearsStorageAndGoesToSignIn()
    {
        var refreshToken = TestSession.NewRefreshToken();
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence(
            () => ProblemResponses.Session(TestSession.NewAccessToken(), refreshToken, _technicianId, "Aisha Karim"),
            ProblemResponses.NoContent));
        await harness.Session.SignInAsync("aisha.karim@fieldservice.example", TestSession.NewPassword(), CancellationToken.None);

        await harness.Session.SignOutAsync(CancellationToken.None);

        var signOut = harness.Stub.Requests[1];
        Assert.Equal("/api/auth/sign-out", signOut.Path);
        Assert.Equal(refreshToken, ProblemResponses.ReadBody(signOut).GetProperty("refreshToken").GetString());
        harness.Storage.Received(1).RemoveAll();
        Assert.Null(harness.Session.AccessToken);
        Assert.Null(harness.Session.User);
        await harness.Navigator.Received(1).GoToAsync(Routes.SignIn);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SignOutClearsStorage_WhenTheSignOutCallFails(bool unreachable)
    {
        var harness = TestSession.Create(new StubHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath.EndsWith("/sign-in", StringComparison.Ordinal)
                ? Task.FromResult(ProblemResponses.Session(TestSession.NewAccessToken(), TestSession.NewRefreshToken(), _technicianId, "Aisha Karim"))
                : unreachable
                    ? Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection refused"))
                    : Task.FromResult(ProblemResponses.ServerError(Guid.NewGuid().ToString("D")))));
        await harness.Session.SignInAsync("aisha.karim@fieldservice.example", TestSession.NewPassword(), CancellationToken.None);

        await harness.Session.SignOutAsync(CancellationToken.None);

        harness.Storage.Received(1).RemoveAll();
        Assert.Null(harness.StoredRefreshToken);
        Assert.Null(harness.Session.AccessToken);
        await harness.Navigator.Received(1).GoToAsync(Routes.SignIn);
    }

    [Fact]
    public async Task SignOutSkipsTheApiButStillClears_WhenNoTokenIsStored()
    {
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence());

        await harness.Session.SignOutAsync(CancellationToken.None);

        Assert.Empty(harness.Stub.Requests);
        harness.Storage.Received(1).RemoveAll();
        await harness.Navigator.Received(1).GoToAsync(Routes.SignIn);
    }

    [Fact]
    public async Task ASignOutTheTechnicianChoseIsNotReportedAsAnExpiredSession()
    {
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence(
            () => ProblemResponses.Session(TestSession.NewAccessToken(), TestSession.NewRefreshToken(), _technicianId, "Aisha Karim"),
            ProblemResponses.NoContent));
        await harness.Session.SignInAsync("aisha.karim@fieldservice.example", TestSession.NewPassword(), CancellationToken.None);

        await harness.Session.SignOutAsync(CancellationToken.None);

        Assert.Equal(SessionEndReason.SignedOut, harness.Session.EndReason);
    }

    [Fact]
    public async Task ARejectedRestoreIsReportedAsAnExpiredSessionUntilTheNextSignIn()
    {
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence(
            ProblemResponses.SessionExpired,
            () => ProblemResponses.Session(TestSession.NewAccessToken(), TestSession.NewRefreshToken(), _technicianId, "Aisha Karim")));
        harness.StoreRefreshToken(TestSession.NewRefreshToken());

        await harness.Session.TryRestoreAsync(CancellationToken.None);
        var afterRestore = harness.Session.EndReason;
        await harness.Session.SignInAsync("aisha.karim@fieldservice.example", TestSession.NewPassword(), CancellationToken.None);

        Assert.Equal(SessionEndReason.Expired, afterRestore);
        Assert.Equal(SessionEndReason.None, harness.Session.EndReason);
    }

    [Fact]
    public async Task SessionLogsHoldIdentifiersOnly()
    {
        var refreshToken = TestSession.NewRefreshToken();
        var accessToken = TestSession.NewAccessToken();
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence(
            () => ProblemResponses.Session(accessToken, refreshToken, _technicianId, "Aisha Karim"),
            ProblemResponses.NoContent));

        await harness.Session.SignInAsync("aisha.karim@fieldservice.example", TestSession.NewPassword(), CancellationToken.None);
        await harness.Session.SignOutAsync(CancellationToken.None);

        var text = harness.Logs.AllText();
        Assert.Contains(_technicianId.ToString(), text, StringComparison.Ordinal);
        Assert.DoesNotContain(refreshToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain(accessToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain("Aisha Karim", text, StringComparison.Ordinal);
        Assert.DoesNotContain("aisha.karim", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignOutRaisesSessionEndedOnce()
    {
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence(
            () => ProblemResponses.Session(TestSession.NewAccessToken(), TestSession.NewRefreshToken(), _technicianId, "Aisha Karim"),
            ProblemResponses.NoContent));
        await harness.Session.SignInAsync("aisha.karim@fieldservice.example", TestSession.NewPassword(), CancellationToken.None);
        var ended = CountEnds(harness.Session);

        await harness.Session.SignOutAsync(CancellationToken.None);

        Assert.Equal(1, ended());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ARefusedRefreshRaisesSessionEndedOnce(HttpStatusCode status)
    {
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence(
            () => ProblemResponses.Session(TestSession.NewAccessToken(), TestSession.NewRefreshToken(), _technicianId, "Aisha Karim"),
            () => Refused(status)));
        await harness.Session.SignInAsync("aisha.karim@fieldservice.example", TestSession.NewPassword(), CancellationToken.None);
        var ended = CountEnds(harness.Session);

        var refreshed = await harness.Session.RefreshAccessTokenAsync(harness.Session.AccessToken, CancellationToken.None);

        Assert.False(refreshed);
        Assert.Equal(1, ended());
        Assert.Equal(SessionEndReason.Expired, harness.Session.EndReason);
        Assert.Null(harness.Session.User);
    }

    [Fact]
    public async Task AnUnreadableStoreDuringARefreshRaisesSessionEndedOnce()
    {
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence(
            () => ProblemResponses.Session(TestSession.NewAccessToken(), TestSession.NewRefreshToken(), _technicianId, "Aisha Karim")));
        await harness.Session.SignInAsync("aisha.karim@fieldservice.example", TestSession.NewPassword(), CancellationToken.None);
        harness.Storage.GetAsync(TokenStore.RefreshTokenKey).ThrowsAsync(new InvalidOperationException("keystore entry unreadable"));
        var ended = CountEnds(harness.Session);

        var refreshed = await harness.Session.RefreshAccessTokenAsync(harness.Session.AccessToken, CancellationToken.None);

        Assert.False(refreshed);
        Assert.Equal(1, ended());
        Assert.Equal(SessionEndReason.Expired, harness.Session.EndReason);
        Assert.Single(harness.Stub.Requests);
        await harness.Navigator.Received(1).GoToAsync(Routes.SignIn);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ARestoreRejectedAtStartupRaisesSessionEndedOnce(HttpStatusCode status)
    {
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence(() => Refused(status)));
        harness.StoreRefreshToken(TestSession.NewRefreshToken());
        var ended = CountEnds(harness.Session);

        var route = await harness.Session.TryRestoreAsync(CancellationToken.None);

        Assert.Equal(Routes.SignIn, route);
        Assert.Equal(1, ended());
        Assert.Equal(SessionEndReason.Expired, harness.Session.EndReason);
    }

    [Fact]
    public async Task ASuccessfulOrUnreachableRefreshRaisesNothing()
    {
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence(
            () => ProblemResponses.Session(TestSession.NewAccessToken(), TestSession.NewRefreshToken(), _technicianId, "Aisha Karim"),
            () => ProblemResponses.Session(TestSession.NewAccessToken(), TestSession.NewRefreshToken(), _technicianId, "Aisha Karim"),
            () => throw new HttpRequestException("Connection refused")));
        await harness.Session.SignInAsync("aisha.karim@fieldservice.example", TestSession.NewPassword(), CancellationToken.None);
        var ended = CountEnds(harness.Session);

        Assert.True(await harness.Session.RefreshAccessTokenAsync(harness.Session.AccessToken, CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() => harness.Session.RefreshAccessTokenAsync(harness.Session.AccessToken, CancellationToken.None));

        Assert.Equal(0, ended());
        Assert.NotNull(harness.Session.User);
    }

    [Fact]
    public async Task ARefreshThatFinishesAfterSignOutWritesNoTokenBackAndRestoresNoUser()
    {
        var heldRefresh = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rotatedRefreshToken = TestSession.NewRefreshToken();
        var rotatedAccessToken = TestSession.NewAccessToken();
        var harness = TestSession.Create(new StubHttpMessageHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            SignInPath => Task.FromResult(ProblemResponses.Session(TestSession.NewAccessToken(), TestSession.NewRefreshToken(), _technicianId, "Aisha Karim")),
            RefreshPath => Hold(refreshStarted, heldRefresh),
            SignOutPath => Task.FromResult(ProblemResponses.NoContent()),
            _ => throw new InvalidOperationException("Unexpected request."),
        }));
        await harness.Session.SignInAsync("aisha.karim@fieldservice.example", TestSession.NewPassword(), CancellationToken.None);
        var ended = CountEnds(harness.Session);

        var refreshing = harness.Session.RefreshAccessTokenAsync(harness.Session.AccessToken, CancellationToken.None);
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await harness.Session.SignOutAsync(CancellationToken.None);
        heldRefresh.SetResult(ProblemResponses.Session(rotatedAccessToken, rotatedRefreshToken, _technicianId, "Aisha Karim"));
        var refreshed = await refreshing;

        Assert.False(refreshed);
        Assert.Null(harness.Session.User);
        Assert.Null(harness.Session.AccessToken);
        Assert.Null(harness.StoredRefreshToken);
        await harness.Storage.DidNotReceive().SetAsync(TokenStore.RefreshTokenKey, rotatedRefreshToken);
        Assert.Equal(SessionEndReason.SignedOut, harness.Session.EndReason);
        await harness.Navigator.Received(1).GoToAsync(Routes.SignIn);
        Assert.Equal(1, ended());
        Assert.Contains("session.refresh-superseded", harness.Logs.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusedRefreshThatFinishesAfterTheNextSignInLeavesTheNewSessionAlone()
    {
        var heldRefresh = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextAccessToken = TestSession.NewAccessToken();
        var nextRefreshToken = TestSession.NewRefreshToken();
        var signIns = 0;
        var harness = TestSession.Create(new StubHttpMessageHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            SignInPath => Task.FromResult(Interlocked.Increment(ref signIns) == 1
                ? ProblemResponses.Session(TestSession.NewAccessToken(), TestSession.NewRefreshToken(), _technicianId, "Aisha Karim")
                : ProblemResponses.Session(nextAccessToken, nextRefreshToken, _nextTechnicianId, "Lena Park")),
            RefreshPath => Hold(refreshStarted, heldRefresh),
            SignOutPath => Task.FromResult(ProblemResponses.NoContent()),
            _ => throw new InvalidOperationException("Unexpected request."),
        }));
        await harness.Session.SignInAsync("aisha.karim@fieldservice.example", TestSession.NewPassword(), CancellationToken.None);
        var ended = CountEnds(harness.Session);

        var refreshing = harness.Session.RefreshAccessTokenAsync(harness.Session.AccessToken, CancellationToken.None);
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await harness.Session.SignOutAsync(CancellationToken.None);
        await harness.Session.SignInAsync("lena.park@fieldservice.example", TestSession.NewPassword(), CancellationToken.None);
        heldRefresh.SetResult(ProblemResponses.SessionExpired());
        var refreshed = await refreshing;

        Assert.False(refreshed);
        Assert.Equal(_nextTechnicianId, harness.Session.User?.Id);
        Assert.Equal(nextAccessToken, harness.Session.AccessToken);
        Assert.Equal(nextRefreshToken, harness.StoredRefreshToken);
        Assert.Equal(SessionEndReason.None, harness.Session.EndReason);
        await harness.Navigator.Received(1).GoToAsync(Routes.SignIn);
        Assert.Equal(1, ended());
    }

    [Fact]
    public async Task ARestoreThatFinishesAfterASignInKeepsTheSignedInTechnician()
    {
        var heldRestore = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var restoreStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextRefreshToken = TestSession.NewRefreshToken();
        var restoredRefreshToken = TestSession.NewRefreshToken();
        var harness = TestSession.Create(new StubHttpMessageHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            SignInPath => Task.FromResult(ProblemResponses.Session(TestSession.NewAccessToken(), nextRefreshToken, _nextTechnicianId, "Lena Park")),
            RefreshPath => Hold(restoreStarted, heldRestore),
            _ => throw new InvalidOperationException("Unexpected request."),
        }));
        harness.StoreRefreshToken(TestSession.NewRefreshToken());

        var restoring = harness.Session.TryRestoreAsync(CancellationToken.None);
        await restoreStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await harness.Session.SignInAsync("lena.park@fieldservice.example", TestSession.NewPassword(), CancellationToken.None);
        heldRestore.SetResult(ProblemResponses.Session(TestSession.NewAccessToken(), restoredRefreshToken, _technicianId, "Aisha Karim"));
        var route = await restoring;

        Assert.Equal(Routes.Jobs, route);
        Assert.Equal(_nextTechnicianId, harness.Session.User?.Id);
        Assert.Equal(nextRefreshToken, harness.StoredRefreshToken);
        await harness.Storage.DidNotReceive().SetAsync(TokenStore.RefreshTokenKey, restoredRefreshToken);
    }

    private static Func<int> CountEnds(ISessionService session)
    {
        var count = 0;
        session.SessionEnded += (_, _) => Interlocked.Increment(ref count);
        return () => Volatile.Read(ref count);
    }

    private static HttpResponseMessage Refused(HttpStatusCode status) =>
        status == HttpStatusCode.BadRequest
            ? ProblemResponses.ValidationFailed(new Dictionary<string, string[]> { ["refreshToken"] = ["The refresh token is not valid."] })
            : ProblemResponses.SessionExpired();

    private static Task<HttpResponseMessage> Hold(TaskCompletionSource started, TaskCompletionSource<HttpResponseMessage> response)
    {
        started.TrySetResult();
        return response.Task;
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(5);
        }

        Assert.True(condition());
    }
}
