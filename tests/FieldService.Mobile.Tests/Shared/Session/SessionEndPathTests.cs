using System.Net;
using System.Text;
using FieldService.Mobile.Shared.Navigation;
using FieldService.Mobile.Shared.Session;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui.Storage;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace FieldService.Mobile.Tests.Shared.Session;

public sealed class SessionEndPathTests
{
    private const string SignOut = "a chosen sign-out";
    private const string RefreshRefused = "a refresh refused with 401";
    private const string RefreshInvalid = "a refresh refused with 400";
    private const string RefreshWithoutToken = "a refresh that finds no stored token";
    private const string RefreshUnreadable = "a refresh that cannot read the store";
    private const string RestoreRejected = "a restore rejected at startup";
    private const string CommandAfterRevocation = "a command answered 401 after a revoked session";
    private const string SignInOverSession = "a sign-in over a live session";

    private const string ClearSessionSignature = "private async Task<EndedSession?> ClearSessionAsync(";
    private const string EndSessionSignature = "private async Task<EndedSession?> EndSessionAsync(";
    private const string RaiseSignature = "private void RaiseSessionEnded()";

    private static readonly Guid _aisha = Guid.Parse("01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e");
    private static readonly Guid _lena = Guid.Parse("01a0f6b1-2a68-77d9-8aa0-8a58bef6a9a0");

    public static TheoryData<string> EndPaths =>
    [
        SignOut,
        RefreshRefused,
        RefreshInvalid,
        RefreshWithoutToken,
        RefreshUnreadable,
        RestoreRejected,
        CommandAfterRevocation,
        SignInOverSession,
    ];

    [Theory]
    [MemberData(nameof(EndPaths))]
    public async Task EveryWayASessionEndsClearsItBeforeItSignalsAndSignalsOnce(string path)
    {
        var harness = TestSession.Create(new StubHttpMessageHandler((request, cancellationToken) => RespondAsync(request, path, cancellationToken)));
        var seen = new List<(Guid? User, string? AccessToken, string? StoredRefreshToken)>();
        harness.Session.SessionEnded += (_, _) => seen.Add((harness.Session.User?.Id, harness.Session.AccessToken, harness.StoredRefreshToken));

        await EndAsync(path, harness);

        var atTheSignal = Assert.Single(seen);
        Assert.Null(atTheSignal.User);
        Assert.Null(atTheSignal.AccessToken);
        Assert.Null(atTheSignal.StoredRefreshToken);
        harness.Storage.Received().RemoveAll();
        if (path == SignInOverSession)
        {
            Assert.Equal(_lena, harness.Session.User?.Id);
            Assert.Equal(SessionEndReason.None, harness.Session.EndReason);
            Assert.Contains("session.replaced " + _aisha, harness.Logs.AllText(), StringComparison.Ordinal);
        }
        else
        {
            Assert.Null(harness.Session.User);
            Assert.Null(harness.Session.AccessToken);
            Assert.Equal(path == SignOut ? SessionEndReason.SignedOut : SessionEndReason.Expired, harness.Session.EndReason);
        }
    }

    [Fact]
    public void OnlyClearSessionAsyncDropsTheSessionAndRaisesTheSignal()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Session", "SessionService.cs"));
        var clear = BodyOf(source, ClearSessionSignature);
        var raise = BodyOf(source, RaiseSignature);

        foreach (var needle in new[] { "_user = null;", "_accessToken = null;", "tokens.Clear();", "RaiseSessionEnded();" })
        {
            var found = IndexesOf(source, needle).ToList();
            Assert.NotEmpty(found);
            Assert.All(found, at => Assert.True(clear.Contains(at), needle + " is used outside ClearSessionAsync."));
        }

        var invoke = Assert.Single(IndexesOf(source, "SessionEnded?.Invoke("));
        Assert.True(raise.Contains(invoke));
    }

    [Fact]
    public void OnlyTheSessionServiceHoldsTheTokenStoreAndOnlyTheTokenStoreHoldsSecureStorage()
    {
        var types = typeof(SessionService).Assembly.GetTypes();

        Assert.Equal([typeof(SessionService)], TakersOf(types, typeof(TokenStore)));
        Assert.Equal([typeof(TokenStore)], TakersOf(types, typeof(ISecureStorage)));
        var removers = RepositoryPaths.MobileSources("*.cs")
            .Where(file => File.ReadAllText(file).Contains("RemoveAll(", StringComparison.Ordinal))
            .Select(RepositoryPaths.ToRelative);
        Assert.Equal(["Shared/Session/TokenStore.cs"], removers);
    }

    [Fact]
    public void OnlyTheSessionServiceSendsATechnicianToSignIn()
    {
        var findings = new List<string>();
        foreach (var file in RepositoryPaths.MobileSources("*.cs"))
        {
            var source = File.ReadAllText(file);
            var relative = RepositoryPaths.ToRelative(file);
            if (source.Contains("\"//sign-in\"", StringComparison.Ordinal))
            {
                findings.Add(relative + ": literal route");
            }

            foreach (var at in IndexesOf(source, "GoToAsync(Routes.SignIn"))
            {
                var allowed = relative == "Shared/Session/SessionService.cs" && BodyOf(source, EndSessionSignature).Contains(at);
                if (!allowed)
                {
                    findings.Add(relative + ": navigation to sign-in");
                }
            }
        }

        Assert.Empty(findings);
        var service = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Session", "SessionService.cs"));
        Assert.Single(IndexesOf(service, "GoToAsync(Routes.SignIn"));
        Assert.Contains("var ended = await ClearSessionAsync(reason, epoch);", Text(service, BodyOf(service, EndSessionSignature)), StringComparison.Ordinal);
    }

    private static async Task EndAsync(string path, TestSession harness)
    {
        if (path == RestoreRejected)
        {
            harness.StoreRefreshToken(TestSession.NewRefreshToken());
            Assert.Equal(Routes.SignIn, await harness.Session.TryRestoreAsync(CancellationToken.None));
            return;
        }

        await harness.Session.SignInAsync("aisha.karim@fieldservice.example", TestSession.NewPassword(), CancellationToken.None);
        switch (path)
        {
            case SignOut:
                await harness.Session.SignOutAsync(CancellationToken.None);
                break;
            case RefreshRefused:
            case RefreshInvalid:
                Assert.Null(await harness.Session.RefreshAccessTokenAsync(harness.Session.Credential, CancellationToken.None));
                break;
            case RefreshWithoutToken:
                harness.Values.Clear();
                Assert.Null(await harness.Session.RefreshAccessTokenAsync(harness.Session.Credential, CancellationToken.None));
                break;
            case RefreshUnreadable:
                harness.Storage.GetAsync(TokenStore.RefreshTokenKey).ThrowsAsync(new InvalidOperationException("keystore entry unreadable"));
                Assert.Null(await harness.Session.RefreshAccessTokenAsync(harness.Session.Credential, CancellationToken.None));
                break;
            case CommandAfterRevocation:
                Assert.Equal(HttpStatusCode.Unauthorized, await SendCommandAsync(harness));
                break;
            case SignInOverSession:
                await harness.Session.SignInAsync("lena.park@fieldservice.example", TestSession.NewPassword(), CancellationToken.None);
                break;
            default:
                throw new InvalidOperationException("Unknown end path.");
        }
    }

    private static async Task<HttpStatusCode> SendCommandAsync(TestSession harness)
    {
        using var client = TestSession.CreateClient(harness.Stub, harness.Logs, harness.Session);
        using var content = new StringContent("{\"status\":\"InProgress\"}", Encoding.UTF8, "application/json");
        using var response = await client.PutAsync("api/jobs/" + JobDtoBuilder.Job1047.ToString("D") + "/status", content, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private static async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, string path, CancellationToken cancellationToken)
    {
        switch (request.RequestUri!.AbsolutePath)
        {
            case "/api/auth/sign-in":
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                var lena = body.Contains("lena.park", StringComparison.Ordinal);
                return ProblemResponses.Session(TestSession.NewAccessToken(), TestSession.NewRefreshToken(), lena ? _lena : _aisha, lena ? "Lena Park" : "Aisha Karim");
            case "/api/auth/sign-out":
                return ProblemResponses.NoContent();
            case "/api/auth/refresh":
                return path == RefreshInvalid
                    ? ProblemResponses.ValidationFailed(new Dictionary<string, string[]> { ["refreshToken"] = ["The refresh token is not valid."] })
                    : ProblemResponses.SessionExpired();
            default:
                return ProblemResponses.Unauthenticated();
        }
    }

    private static List<Type> TakersOf(IEnumerable<Type> types, Type dependency) =>
        [.. types.Where(type => type.GetConstructors().Any(constructor => constructor.GetParameters().Any(parameter => parameter.ParameterType == dependency)))];

    private static SourceRange BodyOf(string source, string signature)
    {
        var at = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, signature + " was not found.");
        var open = source.IndexOf('{', at);
        var depth = 0;
        for (var index = open; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return new SourceRange(open, index);
            }
        }

        throw new InvalidOperationException(signature + " has no closing brace.");
    }

    private static string Text(string source, SourceRange range) => source[range.Start..(range.End + 1)];

    private static IEnumerable<int> IndexesOf(string source, string needle)
    {
        for (var at = source.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = source.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            yield return at;
        }
    }

    private readonly record struct SourceRange(int Start, int End)
    {
        public bool Contains(int index) => index > Start && index < End;
    }
}
