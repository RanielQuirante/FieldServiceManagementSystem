using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FieldService.Mobile.Shared.Formatting;

namespace FieldService.Mobile.Features.Jobs;

public sealed class JobDayGroup : ObservableCollection<JobListItem>
{
    public JobDayGroup(DateOnly day, string heading, IEnumerable<JobListItem> jobs)
        : base(jobs)
    {
        Day = day;
        Heading = heading;
    }

    public DateOnly Day { get; }

    public string Heading { get; }

    public static bool SameDay(JobDayGroup shown, JobDayGroup next)
    {
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentNullException.ThrowIfNull(next);

        return shown.Day == next.Day && shown.Heading == next.Heading;
    }

    public static void Merge(IList<JobDayGroup> shown, IReadOnlyList<JobDayGroup> next)
    {
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentNullException.ThrowIfNull(next);

        for (var index = shown.Count - 1; index >= 0; index--)
        {
            if (!next.Any(group => SameDay(shown[index], group)))
            {
                shown.RemoveAt(index);
            }
        }

        for (var index = 0; index < next.Count; index++)
        {
            var at = IndexOfDay(shown, next[index], index);
            if (at < 0)
            {
                shown.Insert(index, next[index]);
                continue;
            }

            if (at != index)
            {
                var moved = shown[at];
                shown.RemoveAt(at);
                shown.Insert(index, moved);
            }

            shown[index].MergeJobs(next[index]);
        }

        while (shown.Count > next.Count)
        {
            shown.RemoveAt(shown.Count - 1);
        }
    }

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

    private static int IndexOfDay(IList<JobDayGroup> shown, JobDayGroup group, int from)
    {
        for (var index = from; index < shown.Count; index++)
        {
            if (SameDay(shown[index], group))
            {
                return index;
            }
        }

        return -1;
    }

    private void MergeJobs(JobDayGroup next)
    {
        var kept = next.Select(job => job.Id).ToHashSet();
        for (var index = Count - 1; index >= 0; index--)
        {
            if (!kept.Contains(this[index].Id))
            {
                RemoveAt(index);
            }
        }

        for (var index = 0; index < next.Count; index++)
        {
            var at = IndexOfJob(next[index].Id, index);
            if (at < 0)
            {
                Insert(index, next[index]);
                continue;
            }

            if (at != index)
            {
                Move(at, index);
            }

            this[index].Update(next[index]);
        }

        while (Count > next.Count)
        {
            RemoveAt(Count - 1);
        }
    }

    private int IndexOfJob(Guid id, int from)
    {
        for (var index = from; index < Count; index++)
        {
            if (this[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }
}

public sealed partial class JobListItem : ObservableObject
{
    public JobListItem(
        Guid id,
        string jobNumber,
        string title,
        JobStatus status,
        JobPriority priority,
        string customerName,
        string siteAddress,
        string timeWindow,
        string description)
    {
        Id = id;
        JobNumber = jobNumber;
        Title = title;
        Status = status;
        Priority = priority;
        CustomerName = customerName;
        SiteAddress = siteAddress;
        TimeWindow = timeWindow;
        Description = description;
    }

    public Guid Id { get; }

    [ObservableProperty]
    public partial string JobNumber { get; private set; }

    [ObservableProperty]
    public partial string Title { get; private set; }

    [ObservableProperty]
    public partial JobStatus Status { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUrgent))]
    public partial JobPriority Priority { get; private set; }

    [ObservableProperty]
    public partial string CustomerName { get; private set; }

    [ObservableProperty]
    public partial string SiteAddress { get; private set; }

    [ObservableProperty]
    public partial string TimeWindow { get; private set; }

    [ObservableProperty]
    public partial string Description { get; private set; }

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

    public bool ShowsSameAs(JobListItem other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Id == other.Id
            && JobNumber == other.JobNumber
            && Title == other.Title
            && Status == other.Status
            && Priority == other.Priority
            && CustomerName == other.CustomerName
            && SiteAddress == other.SiteAddress
            && TimeWindow == other.TimeWindow
            && Description == other.Description;
    }

    public void Update(JobListItem next)
    {
        ArgumentNullException.ThrowIfNull(next);

        JobNumber = next.JobNumber;
        Title = next.Title;
        Status = next.Status;
        Priority = next.Priority;
        CustomerName = next.CustomerName;
        SiteAddress = next.SiteAddress;
        TimeWindow = next.TimeWindow;
        Description = next.Description;
    }

    public override string ToString() => $"{nameof(JobListItem)} {Id}";
}
