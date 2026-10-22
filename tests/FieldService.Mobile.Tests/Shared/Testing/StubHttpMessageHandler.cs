using System.Collections.Concurrent;
using FieldService.Mobile.Shared.Http;

namespace FieldService.Mobile.Tests.Shared.Testing;

public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;
    private readonly ConcurrentQueue<RecordedRequest> _requests = new();

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        _respond = respond;
    }

    public IReadOnlyList<RecordedRequest> Requests => [.. _requests];

    public static StubHttpMessageHandler Sequence(params Func<HttpResponseMessage>[] responses)
    {
        var queue = new ConcurrentQueue<Func<HttpResponseMessage>>(responses);
        return new StubHttpMessageHandler((_, _) =>
            queue.TryDequeue(out var next)
                ? Task.FromResult(next())
                : throw new InvalidOperationException("The stub has no response left."));
    }

    public static StubHttpMessageHandler Throwing(Exception exception) =>
        new((_, _) => Task.FromException<HttpResponseMessage>(exception));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        _requests.Enqueue(new RecordedRequest(
            request.Method,
            request.RequestUri?.AbsolutePath ?? string.Empty,
            request.RequestUri?.Query ?? string.Empty,
            request.Headers.Authorization?.ToString(),
            CorrelationIdHandler.ReadFrom(request.Headers),
            body));

        var response = await _respond(request, cancellationToken);
        response.RequestMessage ??= request;
        return response;
    }
}

public sealed record RecordedRequest(
    HttpMethod Method,
    string Path,
    string Query,
    string? Authorization,
    string? CorrelationId,
    string? Body);
