using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FieldService.Mobile.Shared.Http;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace FieldService.Mobile.Tests.Shared.Http;

public sealed class HttpLoggingTests
{
    [Fact]
    public async Task LogsTheMethodRouteTemplateStatusElapsedTimeAndCorrelationId()
    {
        var logs = new CapturingLoggerProvider();
        var clock = new FakeTimeProvider();
        using var client = CreateClient(logs, clock, (_, _) =>
        {
            clock.Advance(TimeSpan.FromMilliseconds(125));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/jobs/01a15488-9680-7989-92af-ed07fc34a890");
        request.Options.Set(RequestLoggingHandler.RouteTemplateKey, "/api/jobs/{jobId}");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        var entry = Assert.Single(logs.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("http.response", entry.EventName);
        Assert.Equal("GET", entry.Value("Method"));
        Assert.Equal("/api/jobs/{jobId}", entry.Value("RouteTemplate"));
        Assert.Equal("200", entry.Value("StatusCode"));
        Assert.Equal("125", entry.Value("ElapsedMs"));
        Assert.Equal(CorrelationIdHandler.ReadFrom(request.Headers), entry.Value("CorrelationId"));
    }

    [Fact]
    public async Task NeverLogsHeadersBodiesTokensOrQueryStrings()
    {
        var logs = new CapturingLoggerProvider();
        var accessToken = TestSession.NewAccessToken();
        var refreshToken = TestSession.NewRefreshToken();
        var password = TestSession.NewPassword();
        using var client = CreateClient(logs, new FakeTimeProvider(), (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"{{\"refreshToken\":\"{refreshToken}\"}}", Encoding.UTF8, "application/json"),
        }));
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/technicians/me/jobs?view=active&secret=query-value")
        {
            Content = new StringContent($"{{\"email\":\"aisha.karim@fieldservice.example\",\"password\":\"{password}\"}}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("Cookie", "fs_refresh=" + refreshToken);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        var entry = Assert.Single(logs.Entries);
        Assert.Equal("/api/technicians/me/jobs", entry.Value("RouteTemplate"));
        var text = logs.AllText();
        Assert.DoesNotContain(accessToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain(refreshToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain(password, text, StringComparison.Ordinal);
        Assert.DoesNotContain("aisha.karim", text, StringComparison.Ordinal);
        Assert.DoesNotContain("view=active", text, StringComparison.Ordinal);
        Assert.DoesNotContain("query-value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", text, StringComparison.Ordinal);
        Assert.DoesNotContain("fs_refresh", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LogsTheFailureTypeOnly_WhenTheApiCannotBeReached()
    {
        var logs = new CapturingLoggerProvider();
        using var client = CreateClient(logs, new FakeTimeProvider(), (_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection refused (10.0.2.2:5080)")));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("api/auth/refresh", TestContext.Current.CancellationToken));

        var entry = Assert.Single(logs.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("http.failure", entry.EventName);
        Assert.Equal(nameof(HttpRequestException), entry.Value("Failure"));
        Assert.Null(entry.Exception);
        Assert.DoesNotContain("Connection refused", logs.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LogsNothing_WhenTheCallerCancels()
    {
        var logs = new CapturingLoggerProvider();
        using var cancellation = new CancellationTokenSource();
        using var client = CreateClient(logs, new FakeTimeProvider(), (_, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("api/test", cancellation.Token));

        Assert.Empty(logs.Entries);
    }

    [Fact]
    public void TheRouteTemplateOfARelativeRequestDropsTheQueryString()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("api/technicians/me/jobs?view=completed", UriKind.Relative));

        Assert.Equal("api/technicians/me/jobs", RequestLoggingHandler.RouteTemplateOf(request));
    }

    private static HttpClient CreateClient(
        CapturingLoggerProvider logs,
        TimeProvider clock,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        var handler = new CorrelationIdHandler
        {
            InnerHandler = new RequestLoggingHandler(logs.CreateLogger<RequestLoggingHandler>(), clock)
            {
                InnerHandler = new StubHttpMessageHandler(respond),
            },
        };
        return new HttpClient(handler) { BaseAddress = TestSession.ApiBase };
    }
}
