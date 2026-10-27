using System.Collections.Specialized;
using FieldService.Mobile.Features.Jobs;

namespace FieldService.Mobile.Tests.Shared.Testing;

public sealed class GroupedListWatch
{
    private readonly IReadOnlyList<JobDayGroup> _groups;
    private readonly HashSet<JobDayGroup> _tracked = [];

    public GroupedListWatch(IReadOnlyList<JobDayGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        _groups = groups;
        ((INotifyCollectionChanged)groups).CollectionChanged += OnGroupsChanged;
        foreach (var group in groups)
        {
            Track(group);
        }

        AdapterRows = Rows();
    }

    public int AdapterRows { get; private set; }

    public List<NotifyCollectionChangedEventArgs> GroupChanges { get; } = [];

    public List<NotifyCollectionChangedEventArgs> JobChanges { get; } = [];

    public List<string> Inconsistencies { get; } = [];

    public Action<JobListItem>? ContainerLost { get; set; }

    public IEnumerable<NotifyCollectionChangedAction> Actions => GroupChanges.Concat(JobChanges).Select(change => change.Action);

    public int Rows() => _groups.Sum(group => group.Count + 1);

    private void OnGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        GroupChanges.Add(e);
        var added = e.NewItems?.Cast<JobDayGroup>().ToList() ?? [];
        var removed = e.OldItems?.Cast<JobDayGroup>().ToList() ?? [];
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                AdapterRows += added.Sum(group => group.Count + 1);
                break;
            case NotifyCollectionChangedAction.Remove:
                AdapterRows -= removed.Sum(group => group.Count + 1);
                break;
            case NotifyCollectionChangedAction.Replace when added.Count == removed.Count:
                break;
            case NotifyCollectionChangedAction.Move:
                break;
            default:
                AdapterRows = Rows();
                break;
        }

        foreach (var group in removed)
        {
            Untrack(group);
            foreach (var job in group)
            {
                ContainerLost?.Invoke(job);
            }
        }

        foreach (var group in added)
        {
            Track(group);
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var group in _tracked.ToList())
            {
                Untrack(group);
            }

            foreach (var group in _groups)
            {
                Track(group);
            }
        }

        Check(e.Action, "group");
    }

    private void OnJobsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        JobChanges.Add(e);
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                AdapterRows += e.NewItems!.Count;
                break;
            case NotifyCollectionChangedAction.Remove:
                AdapterRows -= e.OldItems!.Count;
                break;
            case NotifyCollectionChangedAction.Replace:
            case NotifyCollectionChangedAction.Move:
                break;
            default:
                AdapterRows = Rows();
                break;
        }

        if (e.Action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace)
        {
            foreach (var job in e.OldItems!.Cast<JobListItem>())
            {
                ContainerLost?.Invoke(job);
            }
        }

        Check(e.Action, "job");
    }

    private void Check(NotifyCollectionChangedAction action, string level)
    {
        var rows = Rows();
        if (AdapterRows != rows)
        {
            Inconsistencies.Add($"{level} {action}: the adapter holds {AdapterRows} rows, the list has {rows}");
            AdapterRows = rows;
        }
    }

    private void Track(JobDayGroup group)
    {
        if (_tracked.Add(group))
        {
            group.CollectionChanged += OnJobsChanged;
        }
    }

    private void Untrack(JobDayGroup group)
    {
        if (_tracked.Remove(group))
        {
            group.CollectionChanged -= OnJobsChanged;
        }
    }
}
