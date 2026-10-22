using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FieldService.Mobile.Tests.Shared.Testing;

public static class ProblemResponses
{
    public const string ProblemMediaType = "application/problem+json";

    public static HttpResponseMessage Problem(
        HttpStatusCode status,
        string code,
        string title,
        string? correlationId = null,
        IReadOnlyDictionary<string, string[]>? errors = null,
        TimeSpan? retryAfter = null)
    {
        var body = new JsonObject
        {
            ["type"] = "https://tools.ietf.org/html/rfc9110",
            ["title"] = title,
            ["status"] = (int)status,
            ["instance"] = "/api/test",
            ["code"] = code,
            ["correlationId"] = correlationId ?? Guid.NewGuid().ToString("D"),
            ["traceId"] = "00-" + Guid.NewGuid().ToString("N") + "-" + Guid.NewGuid().ToString("N")[..16] + "-00",
        };
        if (errors is not null)
        {
            var map = new JsonObject();
            foreach (var (field, messages) in errors)
            {
                map[field] = new JsonArray([.. messages.Select(message => JsonValue.Create(message))]);
            }

            body["errors"] = map;
        }

        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, ProblemMediaType),
        };
        if (retryAfter is { } wait)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(wait);
        }

        return response;
    }

    public static HttpResponseMessage InvalidCredentials() =>
        Problem(HttpStatusCode.Unauthorized, "auth.invalid-credentials", "The email or password is incorrect.");

    public static HttpResponseMessage SessionExpired() =>
        Problem(HttpStatusCode.Unauthorized, "auth.session-expired", "The session has expired.");

    public static HttpResponseMessage Unauthenticated() =>
        Problem(HttpStatusCode.Unauthorized, "auth.unauthenticated", "Authentication is required.");

    public static HttpResponseMessage WrongApplication() =>
        Problem(HttpStatusCode.Forbidden, "auth.wrong-application", "This account uses the other FieldService app.");

    public static HttpResponseMessage RateLimited(TimeSpan? retryAfter) =>
        Problem(HttpStatusCode.TooManyRequests, "rate-limit.exceeded", "Too many requests.", retryAfter: retryAfter);

    public static HttpResponseMessage NotFound() =>
        Problem(HttpStatusCode.NotFound, "job.not-found", "The job was not found.");

    public static HttpResponseMessage ServerError(string correlationId) =>
        Problem(HttpStatusCode.InternalServerError, "server.error", "An unexpected error occurred.", correlationId);

    public static HttpResponseMessage ValidationFailed(IReadOnlyDictionary<string, string[]> errors) =>
        Problem(HttpStatusCode.BadRequest, "request.validation-failed", "One or more fields are invalid.", errors: errors);

    public static HttpResponseMessage Session(string accessToken, string? refreshToken, Guid userId, string displayName, string role = "Technician")
    {
        var body = new JsonObject
        {
            ["accessToken"] = accessToken,
            ["accessTokenExpiresAt"] = "2026-10-19T14:57:00.000Z",
            ["user"] = new JsonObject
            {
                ["id"] = userId.ToString("D"),
                ["displayName"] = displayName,
                ["role"] = role,
            },
            ["refreshToken"] = refreshToken,
        };

        return Json(HttpStatusCode.OK, body);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, JsonNode body) =>
        new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    public static HttpResponseMessage NoContent() => new(HttpStatusCode.NoContent);

    public static JsonElement ReadBody(RecordedRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var document = JsonDocument.Parse(request.Body ?? "null");
        return document.RootElement.Clone();
    }
}
