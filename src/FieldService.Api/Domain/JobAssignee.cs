namespace FieldService.Api.Domain;

public sealed record JobAssignee(Guid TechnicianId, bool IsActive, bool IsOnShift)
{
    public bool IsAvailableForAssignment => Technician.IsAvailable(IsActive, IsOnShift);

    public static JobAssignee Of(Technician technician) =>
        new(technician.UserId, technician.User.IsActive, technician.IsOnShift);
}
