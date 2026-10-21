namespace FieldService.Api.Domain;

internal static class DomainText
{
    public static string Required(string? value, string name)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException($"{name} must contain a non-white-space character.", name);
        }

        return trimmed;
    }

    public static string? Optional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
