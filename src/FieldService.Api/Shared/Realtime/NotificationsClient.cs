using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using FieldService.Api.Shared.Observability;
using Microsoft.Extensions.Options;

namespace FieldService.Api.Shared.Realtime;

public sealed class NotificationsClient(HttpClient http, IOptions<NotificationsOptions> options)
{
    public const string JobEventsPath = "/internal/job-events";

    public const string WebhookIdHeader = "webhook-id";

    public const string WebhookTimestampHeader = "webhook-timestamp";

    public const string WebhookSignatureHeader = "webhook-signature";

    public const string JsonMediaType = "application/json";

    public const string JsonCharSet = "utf-8";

    public async Task<HttpStatusCode> SendAsync(
        JobEvent jobEvent,
        byte[] body,
        long timestamp,
        string signature,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointOf(options.Value))
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(JsonMediaType) { CharSet = JsonCharSet };
        request.Headers.TryAddWithoutValidation(WebhookIdHeader, jobEvent.Id.ToString());
        request.Headers.TryAddWithoutValidation(WebhookTimestampHeader, timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(WebhookSignatureHeader, signature);
        request.Headers.TryAddWithoutValidation(CorrelationId.HeaderName, jobEvent.CorrelationId);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return response.StatusCode;
    }

    public static Uri EndpointOf(NotificationsOptions options) =>
        new($"{options.BaseUrl!.TrimEnd('/')}{JobEventsPath}", UriKind.Absolute);
}
