using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldService.Api.Shared.Errors;

public sealed class ConcurrencyExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (Classify(exception) is not { } code)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = ErrorCodes.TitleOf(code),
                Extensions = { [ProblemDetailsSetup.CodeMember] = code },
            },
        });

        return true;
    }

    public static string? Classify(Exception exception) => exception switch
    {
        DbUpdateConcurrencyException => ErrorCodes.ConcurrencyConflict,
        DbUpdateException
        {
            InnerException: PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.LockNotAvailable,
            },
        } => ErrorCodes.ConcurrencyConflict,
        DbUpdateException
        {
            InnerException: PostgresException { SqlState: PostgresErrorCodes.ObjectNotInPrerequisiteState },
        } => ErrorCodes.InspectionLocked,
        InvalidOperationException { InnerException: DbUpdateException saveFailure } => Classify(saveFailure),
        _ => null,
    };
}
