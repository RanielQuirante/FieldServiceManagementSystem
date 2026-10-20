namespace FieldService.Api.Domain;

public sealed record JobDetails(
    string Title,
    string Description,
    JobPriority Priority,
    string CustomerName,
    string SiteAddress,
    string? SiteAccessNotes,
    string? ContactName,
    string? ContactPhone,
    string? Equipment,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd,
    Guid ChecklistTemplateId);
