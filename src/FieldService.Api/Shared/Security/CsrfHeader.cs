namespace FieldService.Api.Shared.Security;

public static class CsrfHeader
{
    public const string Name = "X-FieldService-Csrf";

    public const string RequiredValue = "1";

    public static bool IsPresent(HttpRequest request) =>
        request.Headers.TryGetValue(Name, out var values)
        && values.Count == 1
        && string.Equals(values[0], RequiredValue, StringComparison.Ordinal);
}
