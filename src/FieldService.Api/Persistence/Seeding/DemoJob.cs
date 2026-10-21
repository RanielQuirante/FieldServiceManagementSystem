using FieldService.Api.Domain;

namespace FieldService.Api.Persistence.Seeding;

public sealed record DemoJob(
    long Number,
    string Title,
    string Description,
    string CustomerName,
    string SiteAddress,
    JobPriority Priority,
    int TemplateNumber,
    DemoTime ScheduledStart,
    DemoTime ScheduledEnd,
    IReadOnlyList<DemoStep> Steps)
{
    public string? SiteAccessNotes { get; init; }

    public string? ContactName { get; init; }

    public string? ContactPhone { get; init; }

    public string? Equipment { get; init; }
}
