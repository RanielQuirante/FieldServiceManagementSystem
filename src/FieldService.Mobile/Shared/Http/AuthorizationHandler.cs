using System.Net;
using System.Net.Http.Headers;
using FieldService.Mobile.Shared.Session;

namespace FieldService.Mobile.Shared.Http;

public sealed class AuthorizationHandler(ISessionService session, ApiBaseUrl apiBaseUrl) : DelegatingHandler
{
    private const string Scheme = "Bearer";

    public static bool IsApiRequest(Uri? requestUri, Uri apiBase)
    {
        ArgumentNullException.ThrowIfNull(apiBase);

        return requestUri is { IsAbsoluteUri: true }
            && Uri.Compare(requestUri, apiBase, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsApiRequest(request.RequestUri, apiBaseUrl.Value))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var accessToken = session.AccessToken;
        Authorize(request, accessToken);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        bool refreshed;
        try
        {
            refreshed = await session.RefreshAccessTokenAsync(accessToken, cancellationToken);
        }
        catch
        {
            response.Dispose();
            throw;
        }

        if (!refreshed)
        {
            return response;
        }

        response.Dispose();
        Authorize(request, session.AccessToken);
        return await base.SendAsync(request, cancellationToken);
    }

    private static void Authorize(HttpRequestMessage request, string? accessToken) =>
        request.Headers.Authorization = string.IsNullOrEmpty(accessToken) ? null : new AuthenticationHeaderValue(Scheme, accessToken);
}
