using System.Diagnostics;
using FieldService.Api.Shared.Observability;

namespace FieldService.Api.Shared.Errors;

public static class ProblemDetailsSetup
{
    public const string CodeMember = "code";

    public const string CorrelationIdMember = "correlationId";

    public const string TraceIdMember = "traceId";

    public static void Customize(ProblemDetailsContext context)
    {
        var httpContext = context.HttpContext;
        var problem = context.ProblemDetails;
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
    }
}
