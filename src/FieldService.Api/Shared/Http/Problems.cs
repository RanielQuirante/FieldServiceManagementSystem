using FieldService.Api.Domain;
using FieldService.Api.Shared.Errors;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace FieldService.Api.Shared.Http;

public static class Problems
{
    public static ProblemHttpResult Of(HttpContext http, string code, string? detail = null) =>
        TypedResults.Problem(Create(http, code, detail));

    public static ProblemDetails Create(HttpContext http, string code, string? detail = null)
    {
        var status = ErrorCodes.StatusOf(code);
        var problem = new ProblemDetails
        {
            Status = status,
            Title = ErrorCodes.TitleOf(code),
            Type = ErrorCodes.TypeFor(status),
            Detail = detail,
        };
        problem.Extensions[ProblemDetailsSetup.CodeMember] = code;
        return ProblemDetailsSetup.Complete(problem, http);
    }

    public static ValidationProblem FieldError(HttpContext http, string field, string message) =>
        FieldErrors(http, new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    public static ValidationProblem FieldError(HttpContext http, DomainError error)
    {
        if (error.Field is not { } field)
        {
            throw new ArgumentException("Only a domain error with a field is a field error.", nameof(error));
        }

        return FieldError(http, field, error.Message);
    }

    public static ProblemHttpResult Conflict(HttpContext http, DomainError error)
    {
        if (error.IsFieldError)
        {
            throw new ArgumentException("A domain error with a field is a field error, not a conflict.", nameof(error));
        }

        if (ErrorCodes.StatusOf(error.Code) != StatusCodes.Status409Conflict)
        {
            throw new ArgumentException("A domain error without a field must carry a 409 code of the registry.", nameof(error));
        }

        return Of(http, error.Code, error.Message);
    }

    public static ValidationProblem FieldErrors(HttpContext http, IDictionary<string, string[]> errors)
    {
        var problem = new HttpValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
        };
        problem.Extensions[ProblemDetailsSetup.CodeMember] = ErrorCodes.ValidationFailed;
        problem.Title = ErrorCodes.TitleOf(ErrorCodes.ValidationFailed);
        var completed = (HttpValidationProblemDetails)ProblemDetailsSetup.Complete(problem, http);
        return TypedResults.ValidationProblem(
            completed.Errors,
            completed.Detail,
            completed.Instance,
            completed.Title,
            completed.Type,
            completed.Extensions);
    }
}
