using System.Diagnostics;
using FieldService.Api.Shared.Observability;
using Microsoft.AspNetCore.Mvc;

namespace FieldService.Api.Shared.Errors;

public static class ProblemDetailsSetup
{
    public const string CodeMember = "code";

    public const string CorrelationIdMember = "correlationId";

    public const string TraceIdMember = "traceId";

    public static void Customize(ProblemDetailsContext context)
    {
        Complete(context.ProblemDetails, context.HttpContext);
    }

    public static ProblemDetails Complete(ProblemDetails problem, HttpContext httpContext)
    {
        var status = problem.Status ?? httpContext.Response.StatusCode;
        problem.Status = status;

        if (!problem.Extensions.ContainsKey(CodeMember) && ErrorCodes.ForStatus(status) is { } code)
        {
            problem.Extensions[CodeMember] = code;
            problem.Title = ErrorCodes.TitleOf(code) ?? problem.Title;
        }

        problem.Type ??= ErrorCodes.TypeFor(status);
        problem.Instance ??= $"{httpContext.Request.PathBase}{httpContext.Request.Path}";
        problem.Extensions.TryAdd(CorrelationIdMember, CorrelationId.From(httpContext).Value);
        problem.Extensions.TryAdd(TraceIdMember, Activity.Current?.Id ?? httpContext.TraceIdentifier);
        if (problem is HttpValidationProblemDetails validation)
        {
            validation.Errors = NormalizeKeys(validation.Errors);
        }

        return problem;
    }

    private static Dictionary<string, string[]> NormalizeKeys(IDictionary<string, string[]> errors)
    {
        var normalized = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (key, messages) in errors)
        {
            var name = ProblemKeyNormalizer.Normalize(key);
            normalized[name] = normalized.TryGetValue(name, out var existing) ? [.. existing, .. messages] : messages;
        }

        return normalized;
    }
}
