using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FieldService.Api.Shared.Errors;

public sealed class BadRequestExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    private const string InvalidValueMessage = "The value is not valid.";

    private const string InvalidBodyMessage = "The request body is missing or is not valid JSON.";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        var status = badRequest.StatusCode;
        var problem = status == StatusCodes.Status400BadRequest
            ? new HttpValidationProblemDetails(FieldErrors(badRequest))
            : new ProblemDetails();
        problem.Status = status;

        httpContext.Response.StatusCode = status;
        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
        });

        return true;
    }

    private static Dictionary<string, string[]> FieldErrors(BadHttpRequestException exception)
    {
        if (exception.InnerException is JsonException { Path: { Length: > 1 } path })
        {
            return new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [ProblemKeyNormalizer.FromJsonPath(path)] = [InvalidValueMessage],
            };
        }

        return new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [ProblemKeyNormalizer.BodyKey] = [InvalidBodyMessage],
        };
    }
}
