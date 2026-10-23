using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace FieldService.Api.Shared.Errors;

public static class CollectionLimitResolver
{
    public static bool TryResolve(
        Exception? exception,
        [NotNullWhen(true)] out string? key,
        [NotNullWhen(true)] out string? message)
    {
        if (exception is CollectionLimitException { Path: { Length: > 1 } path } limit)
        {
            key = ProblemKeyNormalizer.FromJsonPath(WithoutElementIndex(path));
            message = limit.FieldMessage;
            return true;
        }

        key = null;
        message = null;
        return false;
    }

    private static string WithoutElementIndex(string path)
    {
        var start = path.LastIndexOf('[');
        return path.EndsWith(']') && start > 0 ? path[..start] : path;
    }
}

public sealed class CollectionLimitException(string fieldMessage) : JsonException(fieldMessage)
{
    public string FieldMessage { get; } = fieldMessage;
}
