namespace FieldService.Api.Domain;

public sealed record StatusChangeOption(JobStatus Status, bool RequiresNote);
