using FieldService.Api.Domain;

namespace FieldService.Api.Persistence.Seeding;

public sealed record DemoUser(
    DemoPerson Person,
    string DisplayName,
    string Email,
    UserRole Role,
    Trade? Trade,
    string? PhoneNumber,
    bool IsOnShift);
