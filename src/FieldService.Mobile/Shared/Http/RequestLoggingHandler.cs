using Microsoft.Extensions.Logging;

namespace FieldService.Mobile.Shared.Http;

public sealed partial class RequestLoggingHandler(ILogger<RequestLoggingHandler> logger, TimeProvider timeProvider) : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<string> RouteTemplateKey = new("FieldService.RouteTemplate");

    public static string RouteTemplateOf(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Options.TryGetValue(RouteTemplateKey, out var template) && !string.IsNullOrWhiteSpace(template))
        {
            return template;
        }

        var uri = request.RequestUri;
        if (uri is null)
        {
            return string.Empty;
        }

        if (uri.IsAbsoluteUri)
        {
            return uri.AbsolutePath;
        }

        var text = uri.OriginalString;
        var cut = text.IndexOfAny(['?', '#']);
        return cut < 0 ? text : text[..cut];
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var started = timeProvider.GetTimestamp();
        var method = request.Method.Method;
        var routeTemplate = RouteTemplateOf(request);
        var correlationId = CorrelationIdHandler.ReadFrom(request.Headers);

        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            LogResponse(logger, method, routeTemplate, (int)response.StatusCode, ElapsedMilliseconds(started), correlationId);
            return response;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailure(logger, method, routeTemplate, exception.GetType().Name, ElapsedMilliseconds(started), correlationId);
            throw;
        }
    }

    private long ElapsedMilliseconds(long started) => (long)timeProvider.GetElapsedTime(started).TotalMilliseconds;

    [LoggerMessage(EventName = "http.response", Level = LogLevel.Information, Message = "http.response {Method} {RouteTemplate} {StatusCode} {ElapsedMs} {CorrelationId}")]
    private static partial void LogResponse(ILogger logger, string method, string routeTemplate, int statusCode, long elapsedMs, string? correlationId);

    [LoggerMessage(EventName = "http.failure", Level = LogLevel.Warning, Message = "http.failure {Method} {RouteTemplate} {Failure} {ElapsedMs} {CorrelationId}")]
    private static partial void LogFailure(ILogger logger, string method, string routeTemplate, string failure, long elapsedMs, string? correlationId);
}
