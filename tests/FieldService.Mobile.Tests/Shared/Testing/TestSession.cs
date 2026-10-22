using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using FieldService.Mobile.Features.SignIn;
using FieldService.Mobile.Shared.Http;
using FieldService.Mobile.Shared.Navigation;
using FieldService.Mobile.Shared.Session;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Maui.Storage;
using NSubstitute;

namespace FieldService.Mobile.Tests.Shared.Testing;

public sealed class TestSession
{
    public static readonly Uri ApiBase = new("http://10.0.2.2:5080");

    public static readonly ApiBaseUrl BaseUrl = ApiBaseUrl.Create(ApiBase.OriginalString, "Debug");

    private TestSession(StubHttpMessageHandler stub, CapturingLoggerProvider logs)
    {
        Stub = stub;
        Logs = logs;
        Storage = Substitute.For<ISecureStorage>();
        Storage.GetAsync(Arg.Any<string>()).Returns(call => Task.FromResult(Values.TryGetValue(call.ArgAt<string>(0), out var value) ? value : null));
        Storage.SetAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(call =>
        {
            Values[call.ArgAt<string>(0)] = call.ArgAt<string>(1);
            return Task.CompletedTask;
        });
        Storage.When(storage => storage.RemoveAll()).Do(_ => Values.Clear());
        Navigator = Substitute.For<INavigator>();
        Tokens = new TokenStore(Storage, logs.CreateLogger<TokenStore>());
        Auth = new AuthApiClient(CreateClient(stub, logs, authorize: null));
        Session = new SessionService(Auth, Tokens, Navigator, logs.CreateLogger<SessionService>());
    }

    public StubHttpMessageHandler Stub { get; }

    public CapturingLoggerProvider Logs { get; }

    public ConcurrentDictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    public ISecureStorage Storage { get; }

    public INavigator Navigator { get; }

    public TokenStore Tokens { get; }

    public AuthApiClient Auth { get; }

    public SessionService Session { get; }

    public string? StoredRefreshToken => Values.TryGetValue(TokenStore.RefreshTokenKey, out var value) ? value : null;

    public static TestSession Create(StubHttpMessageHandler stub, CapturingLoggerProvider? logs = null) =>
        new(stub, logs ?? new CapturingLoggerProvider());

    public static string NewRefreshToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string NewAccessToken() => "access-" + Guid.NewGuid().ToString("N");

    public static string NewPassword() => "pw-" + Guid.NewGuid().ToString("N");

    public static HttpClient CreateClient(HttpMessageHandler stub, CapturingLoggerProvider logs, ISessionService? authorize)
    {
        HttpMessageHandler pipeline = new CorrelationIdHandler
        {
            InnerHandler = new RequestLoggingHandler(logs.CreateLogger<RequestLoggingHandler>(), new FakeTimeProvider())
            {
                InnerHandler = stub,
            },
        };
        if (authorize is not null)
        {
            pipeline = new AuthorizationHandler(authorize, BaseUrl) { InnerHandler = pipeline };
        }

        return new HttpClient(pipeline, disposeHandler: false) { BaseAddress = ApiBase };
    }

    public void StoreRefreshToken(string refreshToken) => Values[TokenStore.RefreshTokenKey] = refreshToken;
}
