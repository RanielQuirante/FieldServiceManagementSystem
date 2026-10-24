using FieldService.Mobile.Shared.Formatting;

namespace FieldService.Mobile.Features.Jobs;

public sealed class JobDayGroup : List<JobListItem>
{
    public JobDayGroup(DateOnly day, string heading, IEnumerable<JobListItem> jobs)
        : base(jobs)
    {
        Day = day;
        Heading = heading;
    }

    public DateOnly Day { get; }

    public string Heading { get; }

    public static IReadOnlyList<JobDayGroup> Build(
        IEnumerable<AssignedJobResponse> jobs,
        AssignedJobsView view,
        TimeZoneInfo zone,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        IEnumerable<AssignedJobResponse> ordered = view == AssignedJobsView.Completed
            ? jobs.OrderByDescending(job => GroupInstant(job, view))
            : jobs;
        return [.. ordered
            .GroupBy(job => DisplayFormats.LocalDay(GroupInstant(job, view), zone))
            .Select(group => new JobDayGroup(
                group.Key,
                DisplayFormats.DayGroupLabel(group.Key, today),
                group.Select(job => JobListItem.From(job, zone, today, group.Key))))];
    }

    public static DateTimeOffset GroupInstant(AssignedJobResponse job, AssignedJobsView view)
    {
        ArgumentNullException.ThrowIfNull(job);

        return view == AssignedJobsView.Completed
            ? job.CompletedAt ?? job.CancelledAt ?? job.UpdatedAt
            : job.ScheduledStart;
    }
}

public sealed record JobListItem(
    Guid Id,
    string JobNumber,
    string Title,
    JobStatus Status,
    JobPriority Priority,
    string CustomerName,
    string SiteAddress,
    string TimeWindow,
    string Description)
{
    public bool IsUrgent => Priority == JobPriority.Urgent;

    public static string WellText(AssignedJobResponse job, TimeZoneInfo zone, DateOnly today, DateOnly groupDay)
    {
        ArgumentNullException.ThrowIfNull(job);

        return DisplayFormats.LocalDay(job.ScheduledStart, zone) == groupDay
            ? DisplayFormats.TimeWindow(job.ScheduledStart, job.ScheduledEnd, zone, today)
            : DisplayFormats.Scheduled(job.ScheduledStart, job.ScheduledEnd, zone, today);
    }

    public static JobListItem From(AssignedJobResponse job, TimeZoneInfo zone, DateOnly today, DateOnly groupDay)
    {
        ArgumentNullException.ThrowIfNull(job);

        var number = DisplayFormats.JobNumber(job.Number);
        var day = DisplayFormats.RelativeDay(DisplayFormats.LocalDay(job.ScheduledStart, zone), today);
        var spokenDay = day is "Today" or "Tomorrow" or "Yesterday" ? day.ToLowerInvariant() : day;
        var description = string.Join(
            ", ",
            number,
            job.Title,
            DisplayFormats.StatusLabel(job.Status),
            DisplayFormats.PriorityLabel(job.Priority),
            spokenDay + " " + DisplayFormats.SpokenTimeWindow(job.ScheduledStart, job.ScheduledEnd, zone, today),
            job.CustomerName,
            job.SiteAddress);

        return new JobListItem(
            job.Id,
            number,
            job.Title,
            job.Status,
            job.Priority,
            job.CustomerName,
            job.SiteAddress,
            WellText(job, zone, today, groupDay),
            description);
    }

    public override string ToString() => $"{nameof(JobListItem)} {Id}";
}
