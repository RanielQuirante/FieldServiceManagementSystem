using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using FieldService.Mobile.Shared.Http;

namespace FieldService.Mobile.Features.SignIn;

public sealed class AuthApiClient(HttpClient http)
{
    public const string SignInRoute = "/api/auth/sign-in";
    public const string RefreshRoute = "/api/auth/refresh";
    public const string SignOutRoute = "/api/auth/sign-out";

    public Task<SessionResponse> SignInAsync(SignInRequest request, CancellationToken cancellationToken) =>
        PostForSessionAsync(SignInRoute, JsonContent.Create(request, MobileJsonContext.Default.SignInRequest), cancellationToken);

    public Task<SessionResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        PostForSessionAsync(
            RefreshRoute,
            JsonContent.Create(new RefreshSessionRequest(refreshToken), MobileJsonContext.Default.RefreshSessionRequest),
            cancellationToken);

    public async Task SignOutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var response = await PostAsync(
            SignOutRoute,
            JsonContent.Create(new SignOutRequest(refreshToken), MobileJsonContext.Default.SignOutRequest),
            cancellationToken);
    }

    private async Task<SessionResponse> PostForSessionAsync(string route, HttpContent content, CancellationToken cancellationToken)
    {
        using var response = await PostAsync(route, content, cancellationToken);
        return await ReadAsync(response, MobileJsonContext.Default.SessionResponse, cancellationToken);
    }

    private async Task<HttpResponseMessage> PostAsync(string route, HttpContent content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(route.TrimStart('/'), UriKind.Relative))
        {
            Content = content,
        };
        request.Options.Set(RequestLoggingHandler.RouteTemplateKey, route);

        var response = await http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            throw await ApiException.FromResponseAsync(response, cancellationToken);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken)
        ?? throw new ApiException(response.StatusCode, null, CorrelationIdHandler.ReadFrom(response.Headers), null);
}
