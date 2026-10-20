namespace FieldService.Api.Shared.Errors;

public static class ExceptionStatus
{
    public static int For(Exception exception, HttpContext context)
    {
        if (exception is OperationCanceledException or IOException && context.RequestAborted.IsCancellationRequested)
        {
            return StatusCodes.Status499ClientClosedRequest;
        }

        if (exception is BadHttpRequestException badRequest)
        {
            return badRequest.StatusCode;
        }

        return ConcurrencyExceptionHandler.Classify(exception) is null
            ? StatusCodes.Status500InternalServerError
            : StatusCodes.Status409Conflict;
    }
}
