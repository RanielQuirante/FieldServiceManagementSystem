using FieldService.Api.Domain;

namespace FieldService.Api.Features.Technicians.GetTechnicians;

public sealed record GetTechniciansResponse(IReadOnlyList<TechnicianResponse> Items);

public sealed record TechnicianResponse(
    Guid Id,
    string DisplayName,
    string Email,
    string PhoneNumber,
    Trade Trade,
    bool IsActive,
    bool IsOnShift,
    bool AvailableForAssignment,
    int OpenJobCount,
    TechnicianJobReference? CurrentJob,
    TechnicianJobReference? NextJob);

public sealed record TechnicianJobReference(
    Guid Id,
    long Number,
    JobStatus Status,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd);
