using System.Net;
using FieldService.Mobile.Shared.Http;
using FieldService.Mobile.Shared.Navigation;
using FieldService.Mobile.Shared.Session;
using FieldService.Mobile.Tests.Shared.Testing;
using NSubstitute;

namespace FieldService.Mobile.Tests.Shared.Http;

public sealed class AuthorizationHandlerTests
{
    private const string ProtectedPath = "/api/technicians/me/jobs";
    private const string RefreshPath = "/api/auth/refresh";
    private static readonly Guid _technicianId = Guid.Parse("01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e");

    [Fact]
    public async Task SendsTheAccessTokenAsABearerToken()
    {
        var accessToken = TestSession.NewAccessToken();
        var router = new Router(accessToken, TestSession.NewRefreshToken());
        var harness = await SignedInAsync(router);
        using var client = TestSession.CreateClient(harness.Stub, harness.Logs, harness.Session);

        using var response = await client.GetAsync(ProtectedPath.TrimStart('/'), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = harness.Stub.Requests.Last();
        Assert.Equal("Bearer " + accessToken, request.Authorization);
    }

    [Fact]
    public async Task FiveConcurrentUnauthorizedRequestsShareOneRefreshAndEachRetriesOnceWithTheNewToken()
    {
        var router = new Router(TestSession.NewAccessToken(), TestSession.NewRefreshToken()) { RejectedBeforeRefresh = 5 };
        var harness = await SignedInAsync(router);
        router.Expire();
        using var client = TestSession.CreateClient(harness.Stub, harness.Logs, harness.Session);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.GetAsync(ProtectedPath.TrimStart('/'), TestContext.Current.CancellationToken)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(1, router.RefreshCalls);
        var protectedRequests = harness.Stub.Requests.Where(request => request.Path == ProtectedPath).ToList();
        Assert.Equal(10, protectedRequests.Count);
        Assert.Equal(5, protectedRequests.Count(request => request.Authorization == "Bearer " + router.CurrentAccessToken));
        Assert.Equal(router.CurrentRefreshToken, harness.StoredRefreshToken);
        Assert.Equal(router.CurrentAccessToken, harness.Session.AccessToken);
        await harness.Navigator.DidNotReceive().GoToAsync(Arg.Any<string>());
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task ReturnsASecondUnauthorizedResponseWithoutLooping()
    {
        var router = new Router(TestSession.NewAccessToken(), TestSession.NewRefreshToken()) { AlwaysReject = true };
        var harness = await SignedInAsync(router);
        using var client = TestSession.CreateClient(harness.Stub, harness.Logs, harness.Session);

        using var response = await client.GetAsync(ProtectedPath.TrimStart('/'), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, router.RefreshCalls);
        Assert.Equal(2, harness.Stub.Requests.Count(request => request.Path == ProtectedPath));
    }

    [Fact]
    public async Task ClearsTheSessionAndGoesToSignInOnce_WhenTheRefreshIsRejected()
    {
        var router = new Router(TestSession.NewAccessToken(), TestSession.NewRefreshToken()) { RejectRefresh = true, RejectedBeforeRefresh = 5 };
        var harness = await SignedInAsync(router);
        router.Expire();
        using var client = TestSession.CreateClient(harness.Stub, harness.Logs, harness.Session);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.GetAsync(ProtectedPath.TrimStart('/'), TestContext.Current.CancellationToken)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        Assert.Equal(1, router.RefreshCalls);
        await harness.Navigator.Received(1).GoToAsync(Routes.SignIn);
        harness.Storage.Received(1).RemoveAll();
        Assert.Null(harness.Session.AccessToken);
        Assert.Equal(SessionEndReason.Expired, harness.Session.EndReason);
        Assert.Equal(5, harness.Stub.Requests.Count(request => request.Path == ProtectedPath));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task PassesTheNetworkFailureOnAndKeepsTheSession_WhenTheRefreshCannotReachTheApi()
    {
        var router = new Router(TestSession.NewAccessToken(), TestSession.NewRefreshToken()) { RefreshUnreachable = true };
        var harness = await SignedInAsync(router);
        var storedToken = harness.StoredRefreshToken;
        router.Expire();
        using var client = TestSession.CreateClient(harness.Stub, harness.Logs, harness.Session);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(ProtectedPath.TrimStart('/'), TestContext.Current.CancellationToken));

        Assert.Equal(storedToken, harness.StoredRefreshToken);
        harness.Storage.DidNotReceive().RemoveAll();
        await harness.Navigator.DidNotReceive().GoToAsync(Arg.Any<string>());
    }

    [Theory]
    [InlineData("http://api.attacker.example:5080/api/technicians/me/jobs")]
    [InlineData("http://10.0.2.2:5081/api/technicians/me/jobs")]
    [InlineData("https://10.0.2.2:5080/api/technicians/me/jobs")]
    [InlineData("http://10.0.2.2.attacker.example:5080/api/technicians/me/jobs")]
    public async Task NeverSendsTheBearerTokenToAnotherAuthority(string address)
    {
        var router = new Router(TestSession.NewAccessToken(), TestSession.NewRefreshToken());
        var harness = await SignedInAsync(router);
        using var client = TestSession.CreateClient(harness.Stub, harness.Logs, harness.Session);

        using var response = await client.GetAsync(new Uri(address), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var request = harness.Stub.Requests.Last();
        Assert.Null(request.Authorization);
        Assert.Equal(0, router.RefreshCalls);
        Assert.NotNull(harness.Session.AccessToken);
    }

    [Theory]
    [InlineData("https://fieldservice.example", "https://fieldservice.example/api/jobs", true)]
    [InlineData("https://fieldservice.example/", "https://FieldService.Example:443/api/technicians/me/jobs?view=active", true)]
    [InlineData("http://10.0.2.2:5080", "http://10.0.2.2:5080/api/jobs", true)]
    [InlineData("https://fieldservice.example", "http://fieldservice.example/api/jobs", false)]
    [InlineData("https://fieldservice.example", "https://fieldservice.example:8443/api/jobs", false)]
    [InlineData("https://fieldservice.example", "https://fieldservice.example.attacker.example/api/jobs", false)]
    [InlineData("https://fieldservice.example", "https://maps.fieldservice.example/api/jobs", false)]
    public void OnlyTheApiAuthorityCountsAsAnApiRequest(string apiBase, string address, bool expected)
    {
        Assert.Equal(expected, AuthorizationHandler.IsApiRequest(new Uri(address), new Uri(apiBase)));
    }

    [Fact]
    public void ARelativeOrMissingAddressIsNeverAnApiRequest()
    {
        Assert.False(AuthorizationHandler.IsApiRequest(new Uri("api/jobs", UriKind.Relative), TestSession.ApiBase));
        Assert.False(AuthorizationHandler.IsApiRequest(null, TestSession.ApiBase));
    }

    [Fact]
    public async Task DisposesTheFirstUnauthorizedResponse_WhenTheRefreshThrows()
    {
        var unauthorized = new TrackingContent();
        var harness = TestSession.Create(new StubHttpMessageHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/auth/sign-in" => Task.FromResult(ProblemResponses.Session(TestSession.NewAccessToken(), TestSession.NewRefreshToken(), _technicianId, "Aisha Karim")),
            RefreshPath => Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection refused")),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = unauthorized }),
        }));
        await harness.Session.SignInAsync("aisha.karim@fieldservice.example", TestSession.NewPassword(), CancellationToken.None);
        using var client = TestSession.CreateClient(harness.Stub, harness.Logs, harness.Session);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(ProtectedPath.TrimStart('/'), TestContext.Current.CancellationToken));

        Assert.True(unauthorized.IsDisposed);
    }

    private static async Task<TestSession> SignedInAsync(Router router)
    {
        var harness = TestSession.Create(new StubHttpMessageHandler(router.RespondAsync));
        await harness.Session.SignInAsync("aisha.karim@fieldservice.example", TestSession.NewPassword(), CancellationToken.None);
        return harness;
    }

    private sealed class Router(string accessToken, string refreshToken)
    {
        private readonly TaskCompletionSource _allRejected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _refreshCalls;
        private int _rejected;
        private bool _expired;

        public int RejectedBeforeRefresh { get; init; } = 1;

        public bool AlwaysReject { get; init; }

        public bool RejectRefresh { get; init; }

        public bool RefreshUnreachable { get; init; }

        public int RefreshCalls => Volatile.Read(ref _refreshCalls);

        public string CurrentAccessToken { get; private set; } = accessToken;

        public string CurrentRefreshToken { get; private set; } = refreshToken;

        public void Expire() => _expired = true;

        public async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/auth/sign-in")
            {
                return ProblemResponses.Session(CurrentAccessToken, CurrentRefreshToken, _technicianId, "Aisha Karim");
            }

            if (path == RefreshPath)
            {
                Interlocked.Increment(ref _refreshCalls);
                if (RefreshUnreachable)
                {
                    throw new HttpRequestException("Connection refused");
                }

                await _allRejected.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                if (RejectRefresh)
                {
                    return ProblemResponses.SessionExpired();
                }

                CurrentAccessToken = TestSession.NewAccessToken();
                CurrentRefreshToken = TestSession.NewRefreshToken();
                _expired = false;
                return ProblemResponses.Session(CurrentAccessToken, CurrentRefreshToken, _technicianId, "Aisha Karim");
            }

            var bearer = request.Headers.Authorization?.Parameter;
            if (!AlwaysReject && !_expired && bearer == CurrentAccessToken)
            {
                return ProblemResponses.Json(HttpStatusCode.OK, new System.Text.Json.Nodes.JsonObject { ["items"] = new System.Text.Json.Nodes.JsonArray() });
            }

            if (Interlocked.Increment(ref _rejected) >= RejectedBeforeRefresh)
            {
                _allRejected.TrySetResult();
            }

            return ProblemResponses.Unauthenticated();
        }
    }

    private sealed class TrackingContent() : StringContent("{}")
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
