using FieldService.Api.Domain;

namespace FieldService.Api.Shared.Security;

public sealed record RefreshCredential(SessionClient Channel, string? Token, bool MissingCsrfHeader)
{
    public bool PresentedCookie => Channel == SessionClient.Web && Token is not null;

    public static RefreshCredential Read(HttpRequest request, string? bodyToken)
    {
        if (bodyToken is not null)
        {
            return new RefreshCredential(SessionClient.Mobile, bodyToken, MissingCsrfHeader: false);
        }

        var cookie = RefreshCookie.Read(request);
        return new RefreshCredential(SessionClient.Web, cookie, cookie is not null && !CsrfHeader.IsPresent(request));
    }
}
