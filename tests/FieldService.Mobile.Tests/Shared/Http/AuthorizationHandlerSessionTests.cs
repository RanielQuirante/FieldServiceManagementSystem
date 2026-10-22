using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldService.Mobile.Shared.Navigation;
using FieldService.Mobile.Shared.Session;
using FieldService.Mobile.Tests.Shared.Testing;
using NSubstitute;

namespace FieldService.Mobile.Tests.Shared.Http;

public sealed class AuthorizationHandlerSessionTests
{
    private const string SignInPath = "/api/auth/sign-in";
    private const string RefreshPath = "/api/auth/refresh";
    private const string SignOutPath = "/api/auth/sign-out";
    private const string JobsPath = "/api/technicians/me/jobs";
    private const string AishaEmail = "aisha.karim@fieldservice.example";
    private const string LenaEmail = "lena.park@fieldservice.example";
    private const string StatusBody = "{\"status\":\"InProgress\"}";

    private static readonly Guid _aisha = Guid.Parse("01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e");
    private static readonly Guid _lena = Guid.Parse("01a0f6b1-2a68-77d9-8aa0-8a58bef6a9a0");
    private static readonly string _statusPath = "/api/jobs/" + JobDtoBuilder.Job1047.ToString("D") + "/status";

    [Fact]
    public async Task ARequestAnsweredUnauthorizedAfterTheNextTechnicianSignedInIsNeverRetriedWithTheirToken()
    {
        var (api, harness) = await SignedInAsAishaAsync();
        using var client = TestSession.CreateClient(harness.Stub, harness.Logs, harness.Session);
        var ended = CountEnds(harness.Session);
        var held = api.HoldNextRequest();

        var request = client.GetAsync(JobsPath.TrimStart('/'), TestContext.Current.CancellationToken);
        await held.Started.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await harness.Session.SignOutAsync(CancellationToken.None);
        await harness.Session.SignInAsync(LenaEmail, TestSession.NewPassword(), CancellationToken.None);
        var lenasToken = harness.Session.AccessToken;
        held.Release(ProblemResponses.Unauthenticated());
        using var response = await request;

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Single(harness.Stub.Requests, call => call.Path == JobsPath);
        Assert.DoesNotContain(harness.Stub.Requests, call => call.Authorization == "Bearer " + lenasToken);
        Assert.Equal(0, api.RefreshCalls);
        Assert.Equal(_lena, harness.Session.User?.Id);
        Assert.Equal(lenasToken, harness.Session.AccessToken);
        Assert.Equal(1, ended());
        Assert.Contains("session.retry-withheld", harness.Logs.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARequestWhoseRefreshCompletesAfterTheNextTechnicianSignedInIsNotRetriedUnderTheirSession()
    {
        var (api, harness) = await SignedInAsAishaAsync();
        using var client = TestSession.CreateClient(harness.Stub, harness.Logs, harness.Session);
        api.ExpireAccessTokens();
        var refresh = api.HoldRefresh();

        var request = client.GetAsync(JobsPath.TrimStart('/'), TestContext.Current.CancellationToken);
        await refresh.Started.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await harness.Session.SignOutAsync(CancellationToken.None);
        await harness.Session.SignInAsync(LenaEmail, TestSession.NewPassword(), CancellationToken.None);
        var lenasAccessToken = harness.Session.AccessToken;
        var lenasRefreshToken = harness.StoredRefreshToken;
        refresh.Release();
        using var response = await request;

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var calls = harness.Stub.Requests.Where(call => call.Path == JobsPath).ToList();
        Assert.Single(calls);
        Assert.DoesNotContain(harness.Stub.Requests, call => call.Authorization == "Bearer " + lenasAccessToken);
        Assert.Equal(_lena, harness.Session.User?.Id);
        Assert.Equal(lenasAccessToken, harness.Session.AccessToken);
        Assert.Equal(lenasRefreshToken, harness.StoredRefreshToken);
        Assert.Equal(1, api.RefreshCalls);
    }

    [Fact]
    public async Task ARequestStartedBeforeAnotherTechnicianSignedInOverTheSessionIsNotRetriedUnderTheirs()
    {
        var (api, harness) = await SignedInAsAishaAsync();
        using var client = TestSession.CreateClient(harness.Stub, harness.Logs, harness.Session);
        var ended = CountEnds(harness.Session);
        var held = api.HoldNextRequest();

        var request = client.GetAsync(JobsPath.TrimStart('/'), TestContext.Current.CancellationToken);
        await held.Started.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await harness.Session.SignInAsync(LenaEmail, TestSession.NewPassword(), CancellationToken.None);
        var lenasToken = harness.Session.AccessToken;
        held.Release(ProblemResponses.Unauthenticated());
        using var response = await request;

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Single(harness.Stub.Requests, call => call.Path == JobsPath);
        Assert.DoesNotContain(harness.Stub.Requests, call => call.Authorization == "Bearer " + lenasToken);
        Assert.Equal(0, api.RefreshCalls);
        Assert.Equal(_lena, harness.Session.User?.Id);
        Assert.Equal(1, ended());
    }

    [Fact]
    public async Task ALateUnauthorizedAnswerAfterSignOutStartsNoRefreshAndDoesNotEndTheSessionAgain()
    {
        var (api, harness) = await SignedInAsAishaAsync();
        using var client = TestSession.CreateClient(harness.Stub, harness.Logs, harness.Session);
        var ended = CountEnds(harness.Session);
        var held = api.HoldNextRequest();

        var request = client.GetAsync(JobsPath.TrimStart('/'), TestContext.Current.CancellationToken);
        await held.Started.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await harness.Session.SignOutAsync(CancellationToken.None);
        held.Release(ProblemResponses.Unauthenticated());
        using var response = await request;

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, api.RefreshCalls);
        Assert.Equal(1, ended());
        Assert.Equal(SessionEndReason.SignedOut, harness.Session.EndReason);
        await harness.Navigator.Received(1).GoToAsync(Routes.SignIn);
    }

    [Fact]
    public async Task ARequestStartedBeforeTheSessionWasRestoredIsRetriedForTheRestoredTechnician()
    {
        var api = new TwoTechnicianApi();
        var harness = TestSession.Create(new StubHttpMessageHandler(api.RespondAsync));
        harness.StoreRefreshToken(api.IssueRefreshToken(_aisha));
        using var client = TestSession.CreateClient(harness.Stub, harness.Logs, harness.Session);

        using var response = await client.GetAsync(JobsPath.TrimStart('/'), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var calls = harness.Stub.Requests.Where(call => call.Path == JobsPath).ToList();
        Assert.Equal(2, calls.Count);
        Assert.Null(calls[0].Authorization);
        Assert.Equal("Bearer " + harness.Session.AccessToken, calls[1].Authorization);
        Assert.Equal(_aisha, harness.Session.User?.Id);
    }

    [Fact]
    public async Task ACommandRefusedForAnExpiredAccessTokenIsSentOnceMoreWithItsBodyForTheSameTechnician()
    {
        var (api, harness) = await SignedInAsAishaAsync();
        using var client = TestSession.CreateClient(harness.Stub, harness.Logs, harness.Session);
        var ended = CountEnds(harness.Session);
        api.ExpireAccessTokens();

        using var response = await client.PutAsync(_statusPath.TrimStart('/'), StatusContent(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var commands = harness.Stub.Requests.Where(call => call.Path == _statusPath).ToList();
        Assert.Equal(2, commands.Count);
        Assert.All(commands, command => Assert.Equal(StatusBody, command.Body));
        Assert.All(commands, command => Assert.Equal(HttpMethod.Put, command.Method));
        Assert.Equal("Bearer " + harness.Session.AccessToken, commands[1].Authorization);
        Assert.NotEqual(commands[0].Authorization, commands[1].Authorization);
        Assert.Equal(1, api.RefreshCalls);
        Assert.Equal(0, ended());
    }

    [Fact]
    public async Task ACommandAnsweredUnauthorizedAfterARevokedSessionEndsTheSessionOnceThroughTheSessionService()
    {
        var (api, harness) = await SignedInAsAishaAsync();
        using var client = TestSession.CreateClient(harness.Stub, harness.Logs, harness.Session);
        var ended = CountEnds(harness.Session);
        api.Revoke();

        using var response = await client.PutAsync(_statusPath.TrimStart('/'), StatusContent(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Single(harness.Stub.Requests, call => call.Path == _statusPath);
        Assert.Equal(1, api.RefreshCalls);
        Assert.Equal(1, ended());
        harness.Storage.Received(1).RemoveAll();
        await harness.Navigator.Received(1).GoToAsync(Routes.SignIn);
        Assert.Equal(SessionEndReason.Expired, harness.Session.EndReason);
        Assert.Null(harness.Session.User);
        Assert.Null(harness.Session.AccessToken);
        Assert.Null(harness.StoredRefreshToken);
    }

    [Fact]
    public async Task ACommandRefusedAsForbiddenLeavesTheSessionAlone()
    {
        var (api, harness) = await SignedInAsAishaAsync();
        using var client = TestSession.CreateClient(harness.Stub, harness.Logs, harness.Session);
        var ended = CountEnds(harness.Session);
        var accessToken = harness.Session.AccessToken;
        api.ForbidsCommands = true;

        using var response = await client.PutAsync(_statusPath.TrimStart('/'), StatusContent(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(harness.Stub.Requests, call => call.Path == _statusPath);
        Assert.Equal(0, api.RefreshCalls);
        Assert.Equal(0, ended());
        harness.Storage.DidNotReceive().RemoveAll();
        await harness.Navigator.DidNotReceive().GoToAsync(Arg.Any<string>());
        Assert.Equal(_aisha, harness.Session.User?.Id);
        Assert.Equal(accessToken, harness.Session.AccessToken);
    }

    [Fact]
    public void TheCredentialNeverPrintsItsAccessToken()
    {
        var accessToken = TestSession.NewAccessToken();
        var credential = new SessionCredential(7, _aisha, accessToken);

        Assert.Equal("SessionCredential 7", credential.ToString());
        Assert.DoesNotContain(accessToken, credential.ToString(), StringComparison.Ordinal);
    }

    private static async Task<(TwoTechnicianApi Api, TestSession Harness)> SignedInAsAishaAsync()
    {
        var api = new TwoTechnicianApi();
        var harness = TestSession.Create(new StubHttpMessageHandler(api.RespondAsync));
        await harness.Session.SignInAsync(AishaEmail, TestSession.NewPassword(), CancellationToken.None);
        return (api, harness);
    }

    private static StringContent StatusContent() => new(StatusBody, Encoding.UTF8, "application/json");

    private static Func<int> CountEnds(ISessionService session)
    {
        var count = 0;
        session.SessionEnded += (_, _) => Interlocked.Increment(ref count);
        return () => Volatile.Read(ref count);
    }

    private sealed class Hold
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<HttpResponseMessage?> _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public void Release(HttpResponseMessage? answer = null) => _released.TrySetResult(answer);

        public async Task<HttpResponseMessage?> WaitAsync(CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            return await _released.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }

    private sealed class TwoTechnicianApi
    {
        private readonly ConcurrentDictionary<string, Guid> _accessTokens = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, Guid> _refreshTokens = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<Hold> _requestHolds = new();
        private Hold? _refreshHold;
        private int _refreshCalls;

        public int RefreshCalls => Volatile.Read(ref _refreshCalls);

        public bool RefusesRefresh { get; set; }

        public bool ForbidsCommands { get; set; }

        public void ExpireAccessTokens() => _accessTokens.Clear();

        public void Revoke()
        {
            _accessTokens.Clear();
            RefusesRefresh = true;
        }

        public Hold HoldNextRequest()
        {
            var hold = new Hold();
            _requestHolds.Enqueue(hold);
            return hold;
        }

        public Hold HoldRefresh()
        {
            var hold = new Hold();
            _refreshHold = hold;
            return hold;
        }

        public string IssueRefreshToken(Guid user)
        {
            var refreshToken = TestSession.NewRefreshToken();
            _refreshTokens[refreshToken] = user;
            return refreshToken;
        }

        public async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == SignInPath)
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                return Issue(body.Contains(LenaEmail, StringComparison.Ordinal) ? _lena : _aisha);
            }

            if (path == SignOutPath)
            {
                return ProblemResponses.NoContent();
            }

            if (path == RefreshPath)
            {
                Interlocked.Increment(ref _refreshCalls);
                var presented = ReadRefreshToken(await request.Content!.ReadAsStringAsync(cancellationToken));
                if (Interlocked.Exchange(ref _refreshHold, null) is { } refreshHold && await refreshHold.WaitAsync(cancellationToken) is { } answer)
                {
                    return answer;
                }

                return !RefusesRefresh && presented is not null && _refreshTokens.TryRemove(presented, out var owner)
                    ? Issue(owner)
                    : ProblemResponses.SessionExpired();
            }

            if (_requestHolds.TryDequeue(out var requestHold) && await requestHold.WaitAsync(cancellationToken) is { } held)
            {
                return held;
            }

            var bearer = request.Headers.Authorization?.Parameter;
            if (bearer is null || !_accessTokens.ContainsKey(bearer))
            {
                return ProblemResponses.Unauthenticated();
            }

            return ForbidsCommands && request.Method != HttpMethod.Get
                ? ProblemResponses.Problem(HttpStatusCode.Forbidden, "auth.forbidden", "You do not have access to this resource.")
                : ProblemResponses.Json(HttpStatusCode.OK, new JsonObject());
        }

        private static string? ReadRefreshToken(string body)
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("refreshToken", out var token) ? token.GetString() : null;
        }

        private HttpResponseMessage Issue(Guid user)
        {
            var accessToken = TestSession.NewAccessToken();
            _accessTokens[accessToken] = user;
            return ProblemResponses.Session(accessToken, IssueRefreshToken(user), user, user == _lena ? "Lena Park" : "Aisha Karim");
        }
    }
}
