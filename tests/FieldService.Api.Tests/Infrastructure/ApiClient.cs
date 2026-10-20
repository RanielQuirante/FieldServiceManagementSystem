using System.Net;
using System.Net.Http.Headers;
using FieldService.Api.Shared.Observability;

namespace FieldService.Api.Tests.Infrastructure;

public sealed class ApiClient(HttpClient client) : IDisposable
{
    private static int _addressCounter;

    public IPAddress RemoteAddress { get; } = NextRemoteAddress();

    public string? AccessToken { get; set; }

    public string? LastCorrelationId { get; private set; }

    public Task<HttpResponseMessage> GetAsync(string path)
    {
        return SendAsync(new HttpRequestMessage(HttpMethod.Get, path));
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

    public void Dispose()
    {
        client.Dispose();
    }

    private static IPAddress NextRemoteAddress()
    {
        var next = Interlocked.Increment(ref _addressCounter) & 0xFFFF;
        return new IPAddress([10, 200, (byte)(next >> 8), (byte)(next & 0xFF)]);
    }
}
