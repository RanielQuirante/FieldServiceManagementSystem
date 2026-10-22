using FieldService.Api.Domain;
using FieldService.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Features.Jobs;

public static class AssigneeQuery
{
    public const string Field = "technicianId";

    public const string UnknownMessage = "Choose one of the technicians.";

    public static Task<JobAssignee?> FindAsync(
        FieldServiceDbContext db,
        Guid technicianId,
        CancellationToken cancellationToken) =>
        db.Technicians
            .AsNoTracking()
            .Where(technician => technician.UserId == technicianId)
            .Select(technician => new JobAssignee(technician.UserId, technician.User.IsActive, technician.IsOnShift))
            .SingleOrDefaultAsync(cancellationToken);
}
