using FieldService.Api.Domain;
using FieldService.Api.Persistence;
using FieldService.Api.Shared.Errors;
using FieldService.Api.Shared.Http;
using FieldService.Api.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FieldService.Api.Features.Jobs.GetJobById;

public static class GetJobByIdEndpoint
{
    public static void Map(RouteGroupBuilder jobs) =>
        jobs.MapGet("/{jobId:guid}", HandleAsync)
            .WithName("GetJobById")
            .WithSummary("Get a job")
            .WithDescription("Returns the job with its assignee, its complete history, oldest first, and the version to send with the next command. A technician can read only the jobs assigned to them; any other job returns 404.")
            .RequireAuthorization(Policies.AdministratorOrTechnician)
            .ProducesProblem(StatusCodes.Status404NotFound);

    public static async Task<Results<Ok<JobDetailsResponse>, ProblemHttpResult>> HandleAsync(
        Guid jobId,
        HttpContext http,
        CurrentUser user,
        FieldServiceDbContext db,
        CancellationToken cancellationToken)
    {
        var details = await JobDetailsQuery.FindAsync(db, jobId, new JobActor(user.Id, user.Role), cancellationToken);
        return details is null
            ? Problems.Of(http, ErrorCodes.JobNotFound)
            : TypedResults.Ok(details);
    }
}
