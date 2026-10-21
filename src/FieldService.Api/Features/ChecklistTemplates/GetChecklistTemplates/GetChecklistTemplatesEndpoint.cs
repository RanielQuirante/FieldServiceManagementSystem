using FieldService.Api.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Features.ChecklistTemplates.GetChecklistTemplates;

public static class GetChecklistTemplatesEndpoint
{
    public static void Map(RouteGroupBuilder templates) =>
        templates.MapGet(string.Empty, HandleAsync)
            .WithName("GetChecklistTemplates")
            .WithSummary("List checklist templates")
            .WithDescription("Returns every checklist template, ordered by name, with its trade and item count. The create-job form offers them as the job's checklist.");

    public static async Task<Ok<GetChecklistTemplatesResponse>> HandleAsync(
        FieldServiceDbContext db,
        CancellationToken cancellationToken)
    {
        var items = await db.ChecklistTemplates
            .AsNoTracking()
            .OrderBy(template => template.Name)
            .Select(template => new ChecklistTemplateResponse(
                template.Id,
                template.Name,
                template.Trade,
                template.Items.Count))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new GetChecklistTemplatesResponse(items));
    }
}
