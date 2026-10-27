using System.Collections.ObjectModel;
using System.Collections.Specialized;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Navigation;
using FieldService.Mobile.Shared.Screens;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Features.Jobs;

public sealed class GroupMergeTests
{
    private const string HoldNote = "Waiting for the replacement fan motor quote to be approved.";
    private static readonly DateOnly _today = new(2026, 10, 26);

    public static TheoryData<string> Changes => ["submitted near the end", "removed in the middle", "last job of a day", "added", "status", "midnight", "reordered", "mixed", "emptied", "filled"];

    [Fact]
    public void ARefreshWithTheSameJobsChangesNothingInTheShownList()
    {
        var shown = new ObservableCollection<JobDayGroup>([Group(_today, "TODAY", Item("JOB-1042")), Group(_today.AddDays(1), "TOMORROW", Item("JOB-1047"))]);
        var watch = new GroupedListWatch(shown);

        JobDayGroup.Merge(shown, [Group(_today, "TODAY", Item("JOB-1042")), Group(_today.AddDays(1), "TOMORROW", Item("JOB-1047"))]);

        Assert.Empty(watch.Actions);
    }

    [Fact]
    public void AChangedDayGroupIsMergedJobByJobAndTheListNeverHearsAGroupReplaceOrAReset()
    {
        var shown = new ObservableCollection<JobDayGroup>([Group(_today, "TODAY", Item("JOB-1042")), Group(_today.AddDays(1), "TOMORROW", Item("JOB-1047"))]);
        var tomorrow = shown[1];
        var watch = new GroupedListWatch(shown);

        JobDayGroup.Merge(shown, [Group(_today, "TODAY", Item("JOB-1042")), Group(_today.AddDays(1), "TOMORROW", Item("JOB-1047"), Item("JOB-1053")), Group(_today.AddDays(2), "WED 28 OCT", Item("JOB-1060"))]);

        Assert.Same(tomorrow, shown[1]);
        Assert.Equal([NotifyCollectionChangedAction.Add], watch.JobChanges.Select(change => change.Action));
        Assert.Equal(1, watch.JobChanges[0].NewStartingIndex);
        Assert.Equal([NotifyCollectionChangedAction.Add], watch.GroupChanges.Select(change => change.Action));
        Assert.Equal(2, watch.GroupChanges[0].NewStartingIndex);

        JobDayGroup.Merge(shown, [Group(_today, "TODAY", Item("JOB-1042"))]);

        Assert.Equal([NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Remove], watch.GroupChanges.Select(change => change.Action));
        Assert.Equal([2, 1], watch.GroupChanges.Skip(1).Select(change => change.OldStartingIndex));
        Assert.DoesNotContain(NotifyCollectionChangedAction.Replace, watch.Actions);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, watch.Actions);
        Assert.Equal(["JOB-1042"], shown.SelectMany(group => group).Select(job => job.JobNumber));
        Assert.Empty(watch.Inconsistencies);
    }

    [Theory]
    [MemberData(nameof(Changes))]
    public void EveryNotificationOfAMergeKeepsAGroupedListAdapterInStepWithTheRowsItShows(string change)
    {
        var (before, after) = Scenario(change);
        var shown = new ObservableCollection<JobDayGroup>(before);
        var watch = new GroupedListWatch(shown);

        JobDayGroup.Merge(shown, after);

        Assert.Empty(watch.Inconsistencies);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Replace, watch.Actions);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, watch.Actions);
        Assert.Equal(after.Select(group => group.Heading), shown.Select(group => group.Heading));
        Assert.Equal(after.SelectMany(group => group).Select(job => (job.Id, job.Status, job.TimeWindow)), shown.SelectMany(group => group).Select(job => (job.Id, job.Status, job.TimeWindow)));
        Assert.Equal(watch.Rows(), watch.AdapterRows);
    }

    [Fact]
    public void AGroupReplacedByOneWithFewerJobsLeavesTheAdapterOutOfStepWhichIsWhyTheMergeNeverDoesIt()
    {
        var shown = new ObservableCollection<JobDayGroup>(Scenario("submitted near the end").Before);
        var watch = new GroupedListWatch(shown);

        shown[1] = Scenario("submitted near the end").After[1];

        Assert.Equal(["group Replace: the adapter holds 11 rows, the list has 10"], watch.Inconsistencies);
    }

    [Fact]
    public void AJobStillShownIsTheSameCardWithItsNewValues()
    {
        var shown = new ObservableCollection<JobDayGroup>([Group(_today, "TODAY", Item("JOB-1042"), Item("JOB-1047"))]);
        var card = shown[0][1];
        var raised = new List<string?>();
        card.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        JobDayGroup.Merge(shown, [Group(_today, "TODAY", Item("JOB-1042"), Item("JOB-1047", JobStatus.OnHold))]);

        Assert.Same(card, shown[0][1]);
        Assert.Equal(JobStatus.OnHold, card.Status);
        Assert.Contains(nameof(JobListItem.Status), raised);
        Assert.Contains(nameof(JobListItem.Description), raised);
        Assert.True(card.ShowsSameAs(Item("JOB-1047", JobStatus.OnHold)));
        Assert.False(card.ShowsSameAs(Item("JOB-1047")));
    }

    [Fact]
    public async Task MyJobsKeepsOneListInstanceAndARefreshWithTheSameJobsRaisesNoListChange()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        var list = viewModel.Groups;
        var cards = viewModel.Groups.SelectMany(group => group).ToList();
        var watch = new GroupedListWatch(list);
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Same(list, viewModel.Groups);
        Assert.Empty(watch.Actions);
        Assert.DoesNotContain(nameof(MyJobsViewModel.Groups), raised);
        Assert.Equal(cards, viewModel.Groups.SelectMany(group => group));
        Assert.Equal(["JOB-1042", "JOB-1047", "JOB-1053"], viewModel.Groups.SelectMany(group => group).Select(job => job.JobNumber));
    }

    [Fact]
    public async Task ASubmittedJobLeavesMyJobsAsRemovedRowsSoTheGroupedAdapterNeverLosesStep()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
            () => JobDtoBuilder.Json(new GetAssignedJobsResponse([JobDtoBuilder.Job1042Summary(), JobDtoBuilder.Job1053Summary()]))));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        viewModel.DisappearingCommand.Execute(null);
        var watch = new GroupedListWatch(viewModel.Groups);

        viewModel.ApplyQueryAttributes(new Dictionary<string, object> { [Routes.NoticeParameter] = Routes.JobSubmittedNotice, [Routes.JobNumberParameter] = "JOB-1047" });
        await viewModel.AppearingCommand.ExecuteAsync(null);

        Assert.Equal("JOB-1047 submitted.", viewModel.SuccessTitle);
        Assert.Equal(["JOB-1042", "JOB-1053"], viewModel.Groups.SelectMany(group => group).Select(job => job.JobNumber));
        Assert.Equal([NotifyCollectionChangedAction.Remove], watch.Actions);
        Assert.Empty(watch.Inconsistencies);
    }

    [Fact]
    public async Task InTwoPanesTheSelectedCardStaysSelectedWhenItsJobChangesOnAReload()
    {
        var active = JobDtoBuilder.MarcoActive();
        var onHold = active with { Items = [.. active.Items.Select(job => job.Id == JobDtoBuilder.Job1042 ? job with { Status = JobStatus.OnHold } : job)] };
        var harness = new JobsHarness(
            StubHttpMessageHandler.Sequence(
                () => JobDtoBuilder.Json(active),
                () => JobDtoBuilder.Json(JobDtoBuilder.Details1042()),
                () => JobDtoBuilder.Json(onHold)),
            twoPane: true);
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        var card = viewModel.Groups[0][0];
        viewModel.SelectedJob = card;
        await viewModel.OpenJobCommand.ExecuteAsync(viewModel.SelectedJob);
        LoseSelectionWithItsContainer(viewModel);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Same(card, viewModel.SelectedJob);
        Assert.Equal(JobStatus.OnHold, viewModel.SelectedJob?.Status);
        Assert.False(viewModel.KeepsPaneJob);
    }

    [Fact]
    public async Task InTwoPanesAStatusChangeFromThePaneKeepsTheSelectedCardAndThePaneWithoutReloadingIt()
    {
        var gets = new Queue<Func<HttpResponseMessage>>([
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
            () => JobDtoBuilder.Json(JobDtoBuilder.Details1042()),
            () => JobDtoBuilder.Json(new GetAssignedJobsResponse([JobDtoBuilder.Job1042Summary() with { Status = JobStatus.OnHold }, JobDtoBuilder.Job1047Summary(), JobDtoBuilder.Job1053Summary()]))]);
        var harness = new JobsHarness(
            new StubHttpMessageHandler((request, _) => Task.FromResult(request.Method == HttpMethod.Put ? JobDtoBuilder.Json(OnHold1042()) : gets.Dequeue()())),
            twoPane: true);
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        var card = viewModel.Groups[0][0];
        viewModel.SelectedJob = card;
        await viewModel.OpenJobCommand.ExecuteAsync(card);
        var watch = LoseSelectionWithItsContainer(viewModel);
        var pane = new List<bool>();
        var selections = 0;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MyJobsViewModel.ShowsPaneJob))
            {
                pane.Add(viewModel.ShowsPaneJob);
            }
            else if (e.PropertyName == nameof(MyJobsViewModel.SelectedJob))
            {
                selections++;
            }
        };
        var focus = viewModel.Details.UpdateStatusFocusRequest;
        var avatar = viewModel.AccountFocusRequest;
        viewModel.Details.OpenStatusSheetCommand.Execute(null);
        viewModel.Details.SelectStatusOptionCommand.Execute(viewModel.Details.StatusOptions.First(option => option.RequiresNote));
        viewModel.Details.HoldReason = HoldNote;

        await viewModel.Details.ConfirmStatusCommand.ExecuteAsync(null);
        for (var attempt = 0; attempt < 400 && (harness.Stub.Requests.Count < 4 || viewModel.IsBusy); attempt++)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        Assert.Equal(["GET list", "GET job", "PUT status", "GET list"], harness.Stub.Requests.Select(request => request.Method + " " + (request.Path.EndsWith("/me/jobs", StringComparison.Ordinal) ? "list" : request.Path.EndsWith("/status", StringComparison.Ordinal) ? "status" : "job")));
        Assert.Same(card, viewModel.SelectedJob);
        Assert.Equal(JobStatus.OnHold, card.Status);
        Assert.Equal(0, selections);
        Assert.DoesNotContain(false, pane);
        Assert.Empty(watch.Actions);
        Assert.Equal(JobDtoBuilder.Job1042, viewModel.Details.JobId);
        Assert.Equal(focus + 1, viewModel.Details.UpdateStatusFocusRequest);
        Assert.Equal(avatar, viewModel.AccountFocusRequest);
    }

    [Fact]
    public async Task AConnectivityChangeFromAnotherThreadReachesTheListOnlyThroughTheScreensContext()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())));
        var context = new QueuedSynchronizationContext();
        var viewModel = QueuedSynchronizationContext.CreateOn(context, harness.CreateMyJobs);
        await viewModel.AppearingCommand.ExecuteAsync(null);
        var watch = new GroupedListWatch(viewModel.Groups);

        await Task.Run(() => harness.SetOnline(false), TestContext.Current.CancellationToken);

        Assert.Equal(ScreenState.Content, viewModel.State);
        Assert.Equal(1, context.Pending);

        context.RunPending();

        Assert.Equal(ScreenState.Offline, viewModel.State);
        Assert.Empty(watch.Actions);
    }

    private static GroupedListWatch LoseSelectionWithItsContainer(MyJobsViewModel viewModel) =>
        new(viewModel.Groups)
        {
            ContainerLost = job =>
            {
                if (ReferenceEquals(viewModel.SelectedJob, job))
                {
                    viewModel.SelectedJob = null;
                }
            },
        };

    private static JobDetailsResponse OnHold1042() => JobDtoBuilder.Details1042() with
    {
        Status = JobStatus.OnHold,
        Version = 12,
        Affordances = new JobAffordances([new AllowedStatusChange(JobStatus.InProgress, false)], false),
    };

    private static (IReadOnlyList<JobDayGroup> Before, IReadOnlyList<JobDayGroup> After) Scenario(string change)
    {
        var yesterday = _today.AddDays(-1);
        var tomorrow = _today.AddDays(1);
        IReadOnlyList<JobDayGroup> Shown() =>
        [
            Group(yesterday, "YESTERDAY", Item("JOB-1401"), Item("JOB-1402")),
            Group(_today, "TODAY", Item("JOB-1422"), Item("JOB-1424"), Item("JOB-1427"), Item("JOB-1425"), Item("JOB-1426")),
            Group(tomorrow, "TOMORROW", Item("JOB-1053")),
        ];

        return change switch
        {
            "submitted near the end" => (Shown(), [Shown()[0], Group(_today, "TODAY", Item("JOB-1422"), Item("JOB-1424"), Item("JOB-1425"), Item("JOB-1426")), Shown()[2]]),
            "removed in the middle" => (Shown(), [Group(yesterday, "YESTERDAY", Item("JOB-1402")), Shown()[1], Shown()[2]]),
            "last job of a day" => (Shown(), [Shown()[0], Shown()[1]]),
            "added" => (Shown(), [Shown()[0], Group(_today, "TODAY", Item("JOB-1422"), Item("JOB-1424"), Item("JOB-1430"), Item("JOB-1427"), Item("JOB-1425"), Item("JOB-1426")), Shown()[2], Group(tomorrow.AddDays(1), "WED 28 OCT", Item("JOB-1431"))]),
            "status" => (Shown(), [Shown()[0], Group(_today, "TODAY", Item("JOB-1422"), Item("JOB-1424", JobStatus.InProgress), Item("JOB-1427"), Item("JOB-1425"), Item("JOB-1426")), Shown()[2]]),
            "midnight" => (Shown(), [Group(_today, "YESTERDAY", Item("JOB-1422"), Item("JOB-1424"), Item("JOB-1427"), Item("JOB-1425"), Item("JOB-1426")), Group(tomorrow, "TODAY", Item("JOB-1053"))]),
            "reordered" => (Shown(), [Shown()[0], Group(_today, "TODAY", Item("JOB-1422"), Item("JOB-1427"), Item("JOB-1424"), Item("JOB-1425"), Item("JOB-1426", JobStatus.OnHold)), Shown()[2]]),
            "mixed" => (Shown(), [Group(yesterday.AddDays(-1), "SAT 24 OCT", Item("JOB-1399")), Group(_today, "TODAY", Item("JOB-1424", JobStatus.OnHold), Item("JOB-1425"), Item("JOB-1432")), Group(tomorrow, "TOMORROW", Item("JOB-1053"), Item("JOB-1427"))]),
            "emptied" => (Shown(), []),
            "filled" => ([], Shown()),
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };
    }

    private static JobDayGroup Group(DateOnly day, string heading, params JobListItem[] items) => new(day, heading, items);

    private static JobListItem Item(string number, JobStatus status = JobStatus.Assigned) =>
        new(Guid.Parse("00000000-0000-0000-0000-00000000" + number[^4..]), number, "Title " + number, status, JobPriority.Normal, "Customer", "Site", "9:00 to 11:00 AM", number + ", " + status);
}
