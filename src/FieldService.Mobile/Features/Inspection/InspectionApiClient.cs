using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Http;

namespace FieldService.Mobile.Features.Inspection;

public sealed class InspectionApiClient(HttpClient http)
{
    public const string InspectionRoute = "/api/jobs/{jobId}/inspection";
    public const string SubmissionRoute = "/api/jobs/{jobId}/submission";

    public static string PathFor(string route, Guid jobId) => route.Replace("{jobId}", jobId.ToString("D"), StringComparison.Ordinal);

    public Task<InspectionResponse> GetInspectionAsync(Guid jobId, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, PathFor(InspectionRoute, jobId), InspectionRoute, null, MobileJsonContext.Default.InspectionResponse, cancellationToken);

    public async Task<InspectionResponse> SaveInspectionAsync(Guid jobId, SaveInspectionRequest body, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(body, MobileJsonContext.Default.SaveInspectionRequest);
        return await SendAsync(HttpMethod.Put, PathFor(InspectionRoute, jobId), InspectionRoute, content, MobileJsonContext.Default.InspectionResponse, cancellationToken);
    }

    public async Task<JobDetailsResponse> SubmitJobAsync(Guid jobId, SubmitJobRequest body, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(body, MobileJsonContext.Default.SubmitJobRequest);
        return await SendAsync(HttpMethod.Post, PathFor(SubmissionRoute, jobId), SubmissionRoute, content, MobileJsonContext.Default.JobDetailsResponse, cancellationToken);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, string routeTemplate, HttpContent? content, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(path.TrimStart('/'), UriKind.Relative)) { Content = content };
        request.Options.Set(RequestLoggingHandler.RouteTemplateKey, routeTemplate);

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiException.FromResponseAsync(response, cancellationToken);
        }

        try
        {
            return await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken)
                ?? throw Unreadable(response);
        }
        catch (JsonException)
        {
            throw Unreadable(response);
        }
    }

    private static ApiException Unreadable(HttpResponseMessage response) =>
        new(
            response.StatusCode,
            null,
            CorrelationIdHandler.ReadFrom(response.Headers) ?? CorrelationIdHandler.ReadFrom(response.RequestMessage?.Headers),
            null);
}
