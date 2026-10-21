using FieldService.Api.Features.ChecklistTemplates.GetChecklistTemplates;
using FieldService.Api.Shared.Security;

namespace FieldService.Api.Features.ChecklistTemplates;

public static class ChecklistTemplatesEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var templates = app.MapGroup("/api/checklist-templates")
            .WithTags("ChecklistTemplates")
            .RequireAuthorization(Policies.Administrator);
        templates.ProducesProblem(StatusCodes.Status401Unauthorized);
        templates.ProducesProblem(StatusCodes.Status403Forbidden);
        templates.ProducesProblem(StatusCodes.Status429TooManyRequests);

        GetChecklistTemplatesEndpoint.Map(templates);
    }
}
