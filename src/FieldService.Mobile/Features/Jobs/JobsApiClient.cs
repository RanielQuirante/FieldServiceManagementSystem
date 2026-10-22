using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FieldService.Mobile.Shared.Http;

namespace FieldService.Mobile.Features.Jobs;

public enum AssignedJobsView
{
    Active,
    Completed,
}

public sealed class JobsApiClient(HttpClient http)
{
    public const string AssignedJobsRoute = "/api/technicians/me/jobs";
    public const string JobRoute = "/api/jobs/{jobId}";
    public const string JobStatusRoute = "/api/jobs/{jobId}/status";
    public const string ViewParameter = "view";

    public static string ViewValue(AssignedJobsView view) => view switch
    {
        AssignedJobsView.Completed => "completed",
        _ => "active",
    };

    public async Task<GetAssignedJobsResponse> GetAssignedJobsAsync(AssignedJobsView view, CancellationToken cancellationToken)
    {
        var path = string.Create(CultureInfo.InvariantCulture, $"{AssignedJobsRoute}?{ViewParameter}={ViewValue(view)}");
        return await GetAsync(path, AssignedJobsRoute, MobileJsonContext.Default.GetAssignedJobsResponse, cancellationToken);
    }

    public async Task<JobDetailsResponse> GetJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var path = JobRoute.Replace("{jobId}", jobId.ToString("D"), StringComparison.Ordinal);
        return await GetAsync(path, JobRoute, MobileJsonContext.Default.JobDetailsResponse, cancellationToken);
    }

    public async Task<JobDetailsResponse> UpdateStatusAsync(Guid jobId, UpdateJobStatusRequest body, CancellationToken cancellationToken)
    {
        var path = JobStatusRoute.Replace("{jobId}", jobId.ToString("D"), StringComparison.Ordinal);
        using var content = JsonContent.Create(body, MobileJsonContext.Default.UpdateJobStatusRequest);
        return await SendAsync(HttpMethod.Put, path, JobStatusRoute, content, MobileJsonContext.Default.JobDetailsResponse, cancellationToken);
    }

    private Task<T> GetAsync<T>(string path, string routeTemplate, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, path, routeTemplate, null, typeInfo, cancellationToken);

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
