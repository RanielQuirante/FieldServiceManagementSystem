using FieldService.Api.Shared.Observability;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FieldService.Api.Shared.Errors;

public sealed partial class FallbackExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<FallbackExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        LogUnhandledException(
            logger,
            exception,
            exception.GetType().FullName ?? exception.GetType().Name,
            httpContext.Request.Method,
            $"{httpContext.Request.PathBase}{httpContext.Request.Path}",
            CorrelationId.From(httpContext).Value);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = ErrorCodes.TitleOf(ErrorCodes.ServerError),
                Detail = environment.IsDevelopment() ? exception.Message : null,
                Extensions = { [ProblemDetailsSetup.CodeMember] = ErrorCodes.ServerError },
            },
        });

        return true;
    }

    [LoggerMessage(
        EventId = 2001,
        EventName = "request.unhandled-exception",
        Level = LogLevel.Error,
        Message = "Unhandled {ExceptionType} for {RequestMethod} {RequestPath} with correlation id {CorrelationId}")]
    private static partial void LogUnhandledException(
        ILogger logger,
        Exception exception,
        string exceptionType,
        string requestMethod,
        string requestPath,
        string correlationId);
}
