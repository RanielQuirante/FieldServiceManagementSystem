using System.Net.Http.Headers;

namespace FieldService.Mobile.Shared.Http;

public sealed class CorrelationIdHandler : DelegatingHandler
{
    public const string HeaderName = "X-Correlation-ID";

    public static string? ReadFrom(HttpHeaders? headers) =>
        headers is not null && headers.TryGetValues(HeaderName, out var values) ? values.FirstOrDefault() : null;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.Headers.Remove(HeaderName);
        request.Headers.TryAddWithoutValidation(HeaderName, Guid.NewGuid().ToString("D"));

        return base.SendAsync(request, cancellationToken);
    }
}
