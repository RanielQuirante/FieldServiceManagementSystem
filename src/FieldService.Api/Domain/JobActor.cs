namespace FieldService.Api.Domain;

public readonly record struct JobActor(Guid UserId, UserRole Role)
{
    public static JobActor Administrator(Guid userId) => new(userId, UserRole.Administrator);

    public static JobActor Technician(Guid userId) => new(userId, UserRole.Technician);
}
