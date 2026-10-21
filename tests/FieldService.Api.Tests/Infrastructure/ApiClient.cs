using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Features.Auth;
using FieldService.Api.Shared.Http;
using FieldService.Api.Shared.Observability;
using FieldService.Api.Shared.Security;
using Microsoft.Net.Http.Headers;

namespace FieldService.Api.Tests.Infrastructure;

public sealed class ApiClient(HttpClient client) : IDisposable
{
    public const string SignInPath = "/api/auth/sign-in";

    public const string RefreshPath = "/api/auth/refresh";

    public const string SignOutPath = "/api/auth/sign-out";

    public const string ProblemMediaType = "application/problem+json";

    private static int _addressCounter;

    public static JsonSerializerOptions Json { get; } = CreateJson();

    public IPAddress RemoteAddress { get; } = NextRemoteAddress();

    public string? AccessToken { get; set; }

    public string? LastCorrelationId { get; private set; }

    public Task<HttpResponseMessage> GetAsync(string path)
    {
        return SendAsync(new HttpRequestMessage(HttpMethod.Get, path));
    }

    public Task<HttpResponseMessage> PostJsonAsync<T>(string path, T body, Action<HttpRequestMessage>? configure = null)
    {
        return PostAsync(path, JsonContent.Create(body, options: Json), configure);
    }

    public Task<HttpResponseMessage> PostAsync(
        string path,
        HttpContent? content = null,
        Action<HttpRequestMessage>? configure = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        configure?.Invoke(request);
        return SendAsync(request);
    }

    public Task<HttpResponseMessage> SignInAsync(string email, string password, SessionClient sessionClient)
    {
        return PostJsonAsync(SignInPath, new { email, password, client = sessionClient });
    }

    public Task<HttpResponseMessage> RefreshWithCookieAsync(string? refreshCookie, bool csrfHeader = true)
    {
        return PostAsync(RefreshPath, configure: request => AddWebCredential(request, refreshCookie, csrfHeader));
    }

    public Task<HttpResponseMessage> RefreshWithBodyAsync(string? refreshToken)
    {
        return PostJsonAsync(RefreshPath, new { refreshToken });
    }

    public Task<HttpResponseMessage> SignOutWithCookieAsync(string? refreshCookie, bool csrfHeader = true)
    {
        return PostAsync(SignOutPath, configure: request => AddWebCredential(request, refreshCookie, csrfHeader));
    }

    public Task<HttpResponseMessage> SignOutWithBodyAsync(string? refreshToken)
    {
        return PostJsonAsync(SignOutPath, new { refreshToken });
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        LastCorrelationId = $"test-{Guid.NewGuid():N}";
        request.Headers.Add(CorrelationId.HeaderName, LastCorrelationId);
        request.Headers.Add(TestClientIpStartupFilter.HeaderName, RemoteAddress.ToString());
        if (AccessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static async Task<SessionResponse> ReadSessionAsync(HttpResponseMessage response)
    {
        var session = await response.Content.ReadFromJsonAsync<SessionResponse>(Json, TestContext.Current.CancellationToken);
        return session ?? throw new InvalidOperationException("The response has no session body.");
    }

    public static SetCookieHeaderValue? RefreshCookieOf(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(HeaderNames.SetCookie, out var values))
        {
            return null;
        }

        var cookies = values
            .Select(value => SetCookieHeaderValue.TryParse(value, out var parsed) ? parsed : null)
            .OfType<SetCookieHeaderValue>()
            .Where(cookie => cookie.Name.Equals(RefreshCookie.Name, StringComparison.Ordinal))
            .ToList();
        return cookies.Count == 0 ? null : Assert.Single(cookies);
    }

    public static IReadOnlyList<string> SetCookieHeadersOf(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues(HeaderNames.SetCookie, out var values) ? [.. values] : [];
    }

    public static void AssertRefreshCookieCleared(HttpResponseMessage response)
    {
        var cookie = RefreshCookieOf(response);
        Assert.NotNull(cookie);
        Assert.Equal(string.Empty, cookie.Value.ToString());
        Assert.Equal(TimeSpan.Zero, cookie.MaxAge);
        Assert.Equal(DateTimeOffset.UnixEpoch, cookie.Expires);
        Assert.Equal(RefreshCookie.Path, cookie.Path.ToString());
        Assert.True(cookie.Secure);
        Assert.True(cookie.HttpOnly);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
        Assert.False(cookie.Domain.HasValue);
    }

    public static byte[] RefreshTokenHashOf(string token)
    {
        return SHA256.HashData(Base64Url.DecodeFromChars(token));
    }

    public static async Task<JsonDocument> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(ProblemMediaType, response.Content.Headers.ContentType?.MediaType);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = problem.RootElement;
        Assert.Equal(code, root.GetProperty("code").GetString());
        Assert.Equal((int)status, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrEmpty(root.GetProperty("type").GetString()));
        Assert.False(string.IsNullOrEmpty(root.GetProperty("correlationId").GetString()));
        Assert.False(string.IsNullOrEmpty(root.GetProperty("traceId").GetString()));
        Assert.Equal(response.RequestMessage!.RequestUri!.AbsolutePath, root.GetProperty("instance").GetString());
        return problem;
    }

    public static async Task AssertFieldErrorsAsync(HttpResponseMessage response, params string[] keys)
    {
        using var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "request.validation-failed");
        var actual = problem.RootElement
            .GetProperty("errors")
            .EnumerateObject()
            .Select(member => member.Name)
            .Order(StringComparer.Ordinal);
        Assert.Equal(keys.Order(StringComparer.Ordinal), actual);
    }

    public static string WireInstant(DateTimeOffset instant)
    {
        return instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        client.Dispose();
    }

    private static void AddWebCredential(HttpRequestMessage request, string? refreshCookie, bool csrfHeader)
    {
        if (refreshCookie is not null)
        {
            request.Headers.Add(HeaderNames.Cookie, $"{RefreshCookie.Name}={refreshCookie}");
        }

        if (csrfHeader)
        {
            request.Headers.Add(CsrfHeader.Name, CsrfHeader.RequiredValue);
        }
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        HttpRegistration.ConfigureJson(options);
        return options;
    }

    private static IPAddress NextRemoteAddress()
    {
        var next = Interlocked.Increment(ref _addressCounter) & 0xFFFF;
        return new IPAddress([10, 200, (byte)(next >> 8), (byte)(next & 0xFF)]);
    }
}
