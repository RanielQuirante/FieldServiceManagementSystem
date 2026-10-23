using FieldService.Api.Features.Inspections.GetInspection;
using FieldService.Api.Features.Inspections.SaveInspection;

namespace FieldService.Api.Features.Inspections;

public static class InspectionsEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var inspections = app.MapGroup("/api/jobs/{jobId:guid}/inspection").WithTags("Inspections");
        inspections.ProducesProblem(StatusCodes.Status401Unauthorized);
        inspections.ProducesProblem(StatusCodes.Status403Forbidden);
        inspections.ProducesProblem(StatusCodes.Status429TooManyRequests);

        GetInspectionEndpoint.Map(inspections);
        SaveInspectionEndpoint.Map(inspections);
    }
}
