using FieldService.Api.Features.Diagnostics.ReportClientError;
using FieldService.Api.Shared.Security;

namespace FieldService.Api.Features.Diagnostics;

public static class DiagnosticsEndpoints
{
    public const long RequestBodyLimit = 8 * 1024;

    public static void Map(IEndpointRouteBuilder app)
    {
        var clientErrors = app.MapGroup("/api/client-errors")
            .WithTags("Diagnostics")
            .RequireAuthorization(Policies.Administrator);
        clientErrors.ProducesProblem(StatusCodes.Status401Unauthorized);
        clientErrors.ProducesProblem(StatusCodes.Status403Forbidden);
        clientErrors.ProducesProblem(StatusCodes.Status429TooManyRequests);

        ReportClientErrorEndpoint.Map(clientErrors);
    }
}
