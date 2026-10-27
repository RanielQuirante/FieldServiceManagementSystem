namespace FieldService.Mobile.Features.Jobs;

public sealed record PaneJobHandoff(Guid JobId, JobDetailsResponse? Job, DateTimeOffset? LoadedAt);
