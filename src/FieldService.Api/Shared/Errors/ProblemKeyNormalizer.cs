using System.Text.Json;

namespace FieldService.Api.Shared.Errors;

public static class ProblemKeyNormalizer
{
    public const string BodyKey = "body";

    public static string FromJsonPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path == "$")
        {
            return BodyKey;
        }

        var key = path.StartsWith("$.", StringComparison.Ordinal) ? path[2..]
            : path.StartsWith('$') ? path[1..]
            : path;

        return key.Length == 0 ? BodyKey : Normalize(key);
    }

    public static string Normalize(string key)
    {
        var segments = key.Split('.');
        for (var index = 0; index < segments.Length; index++)
        {
            segments[index] = JsonNamingPolicy.CamelCase.ConvertName(segments[index]);
        }

        return string.Join('.', segments);
    }
}
