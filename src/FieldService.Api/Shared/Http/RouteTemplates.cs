namespace FieldService.Api.Shared.Http;

public static class RouteTemplates
{
    public static string? Of(RouteEndpoint? endpoint)
    {
        if (endpoint?.RoutePattern.RawText is not { } template)
        {
            return null;
        }

        var trimmed = template.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }
}
