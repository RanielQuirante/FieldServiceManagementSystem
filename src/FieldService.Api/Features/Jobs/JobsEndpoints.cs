using FieldService.Api.Features.Jobs.AssignJob;
using FieldService.Api.Features.Jobs.CreateJob;
using FieldService.Api.Features.Jobs.GetJobById;
using FieldService.Api.Features.Jobs.GetJobs;

namespace FieldService.Api.Features.Jobs;

public static class JobsEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var jobs = app.MapGroup("/api/jobs").WithTags("Jobs");
        jobs.ProducesProblem(StatusCodes.Status401Unauthorized);
        jobs.ProducesProblem(StatusCodes.Status403Forbidden);
        jobs.ProducesProblem(StatusCodes.Status429TooManyRequests);

        CreateJobEndpoint.Map(jobs);
        GetJobsEndpoint.Map(jobs);
        GetJobByIdEndpoint.Map(jobs);
        AssignJobEndpoint.Map(jobs);
    }
}
