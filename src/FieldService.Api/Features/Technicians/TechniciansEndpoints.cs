using FieldService.Api.Features.Technicians.GetAssignedJobs;
using FieldService.Api.Features.Technicians.GetTechnicians;

namespace FieldService.Api.Features.Technicians;

public static class TechniciansEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var technicians = app.MapGroup("/api/technicians").WithTags("Technicians");
        technicians.ProducesProblem(StatusCodes.Status401Unauthorized);
        technicians.ProducesProblem(StatusCodes.Status403Forbidden);
        technicians.ProducesProblem(StatusCodes.Status429TooManyRequests);

        GetTechniciansEndpoint.Map(technicians);
        GetAssignedJobsEndpoint.Map(technicians);
    }
}
