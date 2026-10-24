using FieldService.Api.Domain;
using FieldService.Api.Shared.Http;
using FieldService.Api.Shared.Observability;
using FieldService.Api.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace FieldService.Api.Features.Diagnostics.ReportClientError;

public static class ReportClientErrorEndpoint
{
    public static void Map(RouteGroupBuilder clientErrors) =>
        clientErrors.MapPost(string.Empty, HandleAsync)
            .WithName("ReportClientError")
            .WithSummary("Report a client error")
            .WithDescription("Records an error from the web dashboard in the API log as one Warning entry with the source web and the caller's user id. The message, source and stack are scrubbed of tokens, secrets, email addresses and query strings before they are logged. The message holds at most 500 characters, the source and the route at most 200, and the stack at most 2000; the route is the path only, without a query string or fragment. The body holds at most 8 KB, and each user can report at most 10 errors a minute. The response has no body and echoes nothing.")
            .RequireRateLimiting(RateLimitPolicies.ClientErrors)
            .WithMetadata(new RequestSizeLimitAttribute(DiagnosticsEndpoints.RequestBodyLimit))
            .ProducesValidationProblem();

    public static Task<Accepted> HandleAsync(
        ReportClientErrorRequest request,
        HttpContext http,
        CurrentUser user,
        ILoggerFactory loggers)
    {
        SecurityLog.ClientErrorReported(
            loggers.CreateLogger(typeof(ReportClientErrorEndpoint)),
            SecurityLog.WebSource,
            user.Id,
            CorrelationId.From(http).Value,
            ClientErrorScrubber.Scrub(request.Source.Trim()),
            ClientErrorScrubber.ScrubOptional(DomainText.Optional(request.Route)),
            UtcInstant.Truncate(request.OccurredAt.GetValueOrDefault()),
            ClientErrorScrubber.ScrubOptional(DomainText.Optional(request.AppVersion)),
            ClientErrorScrubber.ScrubOptional(DomainText.Optional(request.CorrelationId)),
            ClientErrorScrubber.Scrub(request.Message.Trim()),
            ClientErrorScrubber.ScrubOptional(DomainText.Optional(request.Stack)));

        return Task.FromResult(TypedResults.Accepted((string?)null));
    }
}
