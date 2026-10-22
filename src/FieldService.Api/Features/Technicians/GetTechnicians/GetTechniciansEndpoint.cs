using FieldService.Api.Domain;
using FieldService.Api.Persistence;
using FieldService.Api.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Features.Technicians.GetTechnicians;

public static class GetTechniciansEndpoint
{
    public static void Map(RouteGroupBuilder technicians) =>
        technicians.MapGet(string.Empty, HandleAsync)
            .WithName("GetTechnicians")
            .WithSummary("List technicians")
            .WithDescription("Returns every technician, ordered by name, with trade, shift, whether they can be assigned now, their count of open jobs, their current job in progress or on hold, and their next assigned job.")
            .RequireAuthorization(Policies.Administrator);

    public static async Task<Ok<GetTechniciansResponse>> HandleAsync(
        FieldServiceDbContext db,
        CancellationToken cancellationToken)
    {
        var items = await (
            from technician in db.Technicians.AsNoTracking()
            orderby technician.User.DisplayName, technician.UserId
            select new TechnicianResponse(
                technician.UserId,
                technician.User.DisplayName,
                technician.User.Email,
                technician.PhoneNumber,
                technician.Trade,
                technician.User.IsActive,
                technician.IsOnShift,
                Technician.IsAvailable(technician.User.IsActive, technician.IsOnShift),
                db.Jobs.Count(job => job.AssignedTechnicianId == technician.UserId
                    && (job.Status == JobStatus.Assigned
                        || job.Status == JobStatus.InProgress
                        || job.Status == JobStatus.OnHold)),
                db.Jobs
                    .Where(job => job.AssignedTechnicianId == technician.UserId
                        && (job.Status == JobStatus.InProgress || job.Status == JobStatus.OnHold))
                    .OrderBy(job => job.Status == JobStatus.InProgress ? 0 : 1)
                    .ThenByDescending(job => job.UpdatedAt)
                    .ThenByDescending(job => job.Id)
                    .Select(job => new TechnicianJobReference(
                        job.Id,
                        job.Number,
                        job.Status,
                        job.ScheduledStart,
                        job.ScheduledEnd))
                    .FirstOrDefault(),
                db.Jobs
                    .Where(job => job.AssignedTechnicianId == technician.UserId && job.Status == JobStatus.Assigned)
                    .OrderBy(job => job.ScheduledStart)
                    .ThenBy(job => job.Number)
                    .Select(job => new TechnicianJobReference(
                        job.Id,
                        job.Number,
                        job.Status,
                        job.ScheduledStart,
                        job.ScheduledEnd))
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new GetTechniciansResponse(items));
    }
}
