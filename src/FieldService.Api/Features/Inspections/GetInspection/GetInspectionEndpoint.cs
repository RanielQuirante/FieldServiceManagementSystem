using FieldService.Api.Domain;
using FieldService.Api.Persistence;
using FieldService.Api.Shared.Errors;
using FieldService.Api.Shared.Http;
using FieldService.Api.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FieldService.Api.Features.Inspections.GetInspection;

public static class GetInspectionEndpoint
{
    public static void Map(RouteGroupBuilder inspections) =>
        inspections.MapGet(string.Empty, HandleAsync)
            .WithName("GetInspection")
            .WithSummary("Get the inspection of a job")
            .WithDescription("Returns the checklist of the job's template with every item and its reading fields in position order, the saved answers with their readings, the progress with the count of each result, the submission state, the technician notes and the job's version. It returns the checklist before the first save too, with every answer null and updatedAt null. A technician can read only the inspections of jobs assigned to them; any other job returns 404.")
            .RequireAuthorization(Policies.AdministratorOrTechnician)
            .ProducesProblem(StatusCodes.Status404NotFound);

    public static async Task<Results<Ok<InspectionResponse>, ProblemHttpResult>> HandleAsync(
        Guid jobId,
        HttpContext http,
        CurrentUser user,
        FieldServiceDbContext db,
        CancellationToken cancellationToken)
    {
        var inspection = await InspectionQuery.FindAsync(db, jobId, new JobActor(user.Id, user.Role), cancellationToken);
        return inspection is null
            ? Problems.Of(http, ErrorCodes.JobNotFound)
            : TypedResults.Ok(inspection);
    }
}
