using FieldService.Api.Domain;

namespace FieldService.Api.Features.Jobs;

public sealed record JobAffordances(
    IReadOnlyList<AllowedStatusChange> AllowedStatusChanges,
    SubmitAffordance CanSubmit,
    bool CanAssign);

public sealed record AllowedStatusChange(JobStatus Status, bool RequiresNote);
