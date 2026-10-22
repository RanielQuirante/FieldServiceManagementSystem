using System.Net;
using System.Text.Json;

namespace FieldService.Mobile.Shared.Http;

public sealed class ApiException : Exception
{
    public ApiException(HttpStatusCode statusCode, ApiProblem? problem, string? correlationId, TimeSpan? retryAfter)
        : base($"The API answered {(int)statusCode} {problem?.Code ?? "without a problem code"}.")
    {
        StatusCode = statusCode;
        Problem = problem;
        CorrelationId = correlationId;
        RetryAfter = retryAfter;
    }

    public HttpStatusCode StatusCode { get; }

    public ApiProblem? Problem { get; }

    public string? Code => Problem?.Code;

    public string? CorrelationId { get; }

    public TimeSpan? RetryAfter { get; }

    public static async Task<ApiException> FromResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        var problem = await ReadProblemAsync(response, cancellationToken);
        var correlationId = problem?.CorrelationId
            ?? CorrelationIdHandler.ReadFrom(response.Headers)
            ?? CorrelationIdHandler.ReadFrom(response.RequestMessage?.Headers);

        return new ApiException(response.StatusCode, problem, correlationId, response.Headers.RetryAfter?.Delta);
    }

    private static async Task<ApiProblem?> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not ("application/problem+json" or "application/json"))
        {
            return null;
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync(stream, MobileJsonContext.Default.ApiProblem, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
