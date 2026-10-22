namespace FieldService.Mobile.Shared.Http;

public sealed record ApiProblem(
    string? Type,
    string? Title,
    int? Status,
    string? Detail,
    string? Instance,
    string? Code,
    string? CorrelationId,
    string? TraceId,
    IReadOnlyDictionary<string, string[]>? Errors)
{
    public const string ValidationFailedCode = "request.validation-failed";

    public string? FirstError(string field) =>
        Errors is not null && Errors.TryGetValue(field, out var messages) && messages.Length > 0 ? messages[0] : null;

    public IReadOnlyList<string> ErrorsExcept(params string[] fields) =>
        Errors is null
            ? []
            : [.. Errors
                .Where(entry => !fields.Contains(entry.Key, StringComparer.Ordinal))
                .SelectMany(entry => entry.Value)];
}
