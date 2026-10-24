using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Navigation;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using NSubstitute;

namespace FieldService.Mobile.Tests.Features.Jobs;

public sealed class CancelledJobTests
{
    private const string Reason = "Building management replaced the filters under its own maintenance contract.";
    private const string CancelledOnFriday = "Cancelled on Fri 16 Oct at 9:20 AM";

    private static readonly UserReference _dana = new(JobDtoBuilder.Dana, "Dana Whitfield");
    private static readonly UserReference _marco = new(JobDtoBuilder.Marco, "Marco Reyes");

    [Fact]
    public async Task ACancelledJobShowsTheReasonUnderTheStatusLineAndReadsBothAsOneDescription()
    {
        var (viewModel, _) = await OpenAsync(JobDtoBuilder.Details1036());

        Assert.Equal(CancelledOnFriday, viewModel.StatusLine);
        Assert.True(viewModel.HasStatusLine);
        Assert.Equal("“" + Reason + "”", viewModel.ReasonLine);
        Assert.True(viewModel.HasReasonLine);
        Assert.Equal(CancelledOnFriday + ". " + Reason, viewModel.StatusDescription);
        Assert.True(viewModel.HasStatusDescription);
    }

    [Fact]
    public async Task ACancellationTodayReadsCancelledAtTheTimeInTheLineAndTheDescription()
    {
        var job = JobDtoBuilder.Details1036();
        var (viewModel, _) = await OpenAsync(job with
        {
            CancelledAt = JobDtoBuilder.Local(10, 19, 9, 20),
            History = [.. job.History.Take(2), Cancelled(Reason, JobDtoBuilder.Local(10, 19, 9, 20))],
        });

        Assert.Equal("Cancelled at 9:20 AM", viewModel.StatusLine);
        Assert.Equal("Cancelled at 9:20 AM. " + Reason, viewModel.StatusDescription);
    }

    [Fact]
    public async Task TheReasonIsTheNoteOfTheNewestCancelledEntryNeverAnEarlierHoldOrCancellation()
    {
        var (viewModel, _) = await OpenAsync(CancelledWith(
            JobDtoBuilder.History(JobHistoryAction.PutOnHold, JobStatus.InProgress, JobStatus.OnHold, _marco, null, "Waiting for the roof key.", JobDtoBuilder.Local(10, 16, 8, 0)),
            Cancelled("An earlier note.", JobDtoBuilder.Local(10, 16, 9, 0)),
            Cancelled("  " + Reason + "\n", JobDtoBuilder.Local(10, 16, 9, 20))));

        Assert.Equal("“" + Reason + "”", viewModel.ReasonLine);
        Assert.Equal(CancelledOnFriday + ". " + Reason, viewModel.StatusDescription);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ACancelledEntryWithoutANoteHidesTheReasonEvenAfterAHoldNoteAndTheDescriptionIsTheStatusLine(string? note)
    {
        var (viewModel, _) = await OpenAsync(CancelledWith(
            JobDtoBuilder.History(JobHistoryAction.PutOnHold, JobStatus.InProgress, JobStatus.OnHold, _marco, null, "Waiting for the roof key.", JobDtoBuilder.Local(10, 16, 8, 0)),
            Cancelled(note, JobDtoBuilder.Local(10, 16, 9, 20))));

        Assert.Null(viewModel.ReasonLine);
        Assert.False(viewModel.HasReasonLine);
        Assert.Equal(CancelledOnFriday, viewModel.StatusDescription);
        Assert.True(viewModel.HasStatusDescription);
    }

    [Fact]
    public async Task OnlyACancelledJobJoinsItsStatusLineAndReasonInOneDescription()
    {
        var (onHold, _) = await OpenAsync(JobDtoBuilder.Details1037());
        var (inProgress, _) = await OpenAsync(JobDtoBuilder.Details1042());
        var (completed, _) = await OpenAsync(JobDtoBuilder.Details1039());

        Assert.True(onHold.HasReasonLine);
        Assert.All(new[] { onHold, inProgress, completed }, viewModel =>
        {
            Assert.Null(viewModel.StatusDescription);
            Assert.Null(viewModel.PaneStatusDescription);
            Assert.False(viewModel.HasStatusDescription);
        });
        Assert.Null(inProgress.ReasonLine);
        Assert.Null(completed.ReasonLine);
    }

    [Fact]
    public void TheReasonNoteFollowsTheStatusOfTheJob()
    {
        var hold = JobDtoBuilder.History(JobHistoryAction.PutOnHold, JobStatus.InProgress, JobStatus.OnHold, _marco, null, "Waiting for the roof key.", JobDtoBuilder.Local(10, 16, 8, 0));
        var cancel = Cancelled(Reason, JobDtoBuilder.Local(10, 16, 9, 20));
        var job = JobDtoBuilder.Details1036() with { History = [hold, cancel] };

        Assert.Equal(Reason, JobDetailsViewModel.ReasonNoteFor(job));
        Assert.Equal("Waiting for the roof key.", JobDetailsViewModel.ReasonNoteFor(job with { Status = JobStatus.OnHold }));
        Assert.Null(JobDetailsViewModel.ReasonNoteFor(job with { Status = JobStatus.InProgress }));
        Assert.Null(JobDetailsViewModel.ReasonNoteFor(job with { Status = JobStatus.Completed }));
    }

    [Fact]
    public async Task ACancelledJobHasNoDockNoActionRowAndNoStatusActionButTheSiteKeepsNavigate()
    {
        var (viewModel, harness) = await OpenAsync(JobDtoBuilder.Details1036());

        Assert.False(viewModel.ShowsDock);
        Assert.False(viewModel.ShowsHeroActions);
        Assert.False(viewModel.ShowsStartJob);
        Assert.False(viewModel.ShowsUpdateStatus);
        Assert.False(viewModel.ShowsInspectionAction);
        Assert.False(viewModel.ShowsNavigationRow);
        Assert.Null(viewModel.DockHelper);
        Assert.True(viewModel.ShowsRowActions);
        Assert.False(viewModel.CanCall);
        Assert.False(viewModel.ShowsRowCall);
        Assert.Equal("Navigate to 12 Summit Plaza, Floor 4", viewModel.NavigateLabel);

        await viewModel.NavigateCommand.ExecuteAsync(null);

        await harness.Map.Received(1).TryOpenAsync(Arg.Is<Placemark>(place => place.Thoroughfare == "12 Summit Plaza, Floor 4"), Arg.Any<MapLaunchOptions>());
    }

    [Fact]
    public async Task ACancelledJobWithAContactPhoneKeepsCallInTheContactRow()
    {
        var (viewModel, harness) = await OpenAsync(JobDtoBuilder.Details1036() with { ContactPhone = "(555) 014-4417" });

        Assert.True(viewModel.ShowsRowCall);
        Assert.Equal("Call Summit Coworking", viewModel.CallLabel);
        Assert.False(viewModel.ShowsDock);

        viewModel.CallCommand.Execute(null);

        harness.Dialer.Received(1).Open("(555) 014-4417");
    }

    [Fact]
    public async Task OnTheWindowsPaneACancelledJobHasNoActionRowAndItsMetaNamesTheCustomerWithTheReasonUnderIt()
    {
        var (viewModel, _) = await OpenAsync(JobDtoBuilder.Details1036(), inPane: true);

        Assert.True(viewModel.ShowsPaneHero);
        Assert.True(viewModel.ShowsPaneMeta);
        Assert.False(viewModel.ShowsHeroActions);
        Assert.False(viewModel.ShowsDock);
        Assert.Equal(CancelledOnFriday + Spaces.Text("_· Summit Coworking"), viewModel.PaneMeta);
        Assert.Equal("“" + Reason + "”", viewModel.ReasonLine);
        Assert.Equal(CancelledOnFriday + ", Summit Coworking. " + Reason, viewModel.PaneStatusDescription);
    }

    [Fact]
    public async Task ACancelledJobWithNoSavedAnswerShowsTheChecklistRowAndTheChecklistCannotBeOpened()
    {
        var (viewModel, harness) = await OpenAsync(JobDtoBuilder.Details1036());

        Assert.True(viewModel.ShowsChecklistRow);
        Assert.False(viewModel.ShowsChecklistLink);
        Assert.Equal("Rooftop HVAC service", viewModel.ChecklistName);

        await viewModel.OpenChecklistCommand.ExecuteAsync(null);

        await harness.Navigator.DidNotReceiveWithAnyArgs().GoToAsync(default!, default!);
    }

    [Theory]
    [InlineData(1, "1 of 10 answered_· not submitted")]
    [InlineData(6, "6 of 10 answered_· not submitted")]
    public async Task ACancelledJobWithSavedAnswersShowsTheLinkCardThatOpensTheReadOnlyChecklist(int answered, string pattern)
    {
        var subline = Spaces.Text(pattern);
        var job = JobDtoBuilder.Details1036();
        var (viewModel, harness) = await OpenAsync(job with
        {
            Affordances = job.Affordances with { CanSubmit = new SubmitAffordance(false, SubmitAffordance.InvalidTransitionCode, answered, 10) },
        });

        Assert.True(viewModel.ShowsChecklistLink);
        Assert.False(viewModel.ShowsChecklistRow);
        Assert.Equal(subline, viewModel.ChecklistSubline);
        Assert.Equal("Rooftop HVAC service, " + subline + ", opens the inspection", viewModel.ChecklistLinkDescription);

        await viewModel.OpenChecklistCommand.ExecuteAsync(null);

        await harness.Navigator.Received(1).GoToAsync(Routes.InspectionPage, Arg.Is<IDictionary<string, object>>(parameters =>
            (Guid)parameters[Routes.JobIdParameter] == JobDtoBuilder.Job1036));
    }

    [Fact]
    public async Task TheCompletedSegmentListsJob1036UnderItsCancellationDayWithTheCancelledChipAndItsSlot()
    {
        var viewModel = await CompletedAsync(JobDtoBuilder.MarcoCompleted());

        var group = Assert.Single(viewModel.Groups);
        var card = Assert.Single(group);
        Assert.Equal("Fri 16 Oct", group.Heading);
        Assert.Equal(new DateOnly(2026, 10, 16), group.Day);
        Assert.Equal(JobStatus.Cancelled, card.Status);
        Assert.Equal(JobPriority.Low, card.Priority);
        Assert.Equal("2:00–3:00 PM", card.TimeWindow);
        Assert.Equal(
            "JOB-1036, Office AC filter replacement, Cancelled, Low, Fri 16 Oct 2:00 to 3:00 PM, Summit Coworking, 12 Summit Plaza, Floor 4",
            card.Description);
    }

    [Fact]
    public async Task TheCompletedWellAddsTheShortDateOnlyWhenTheSlotIsOnAnotherDayThanItsGroup()
    {
        var viewModel = await CompletedAsync(new GetAssignedJobsResponse([JobDtoBuilder.Job1036Summary(), CompletedToday1037(), CancelledToday1053()]));

        Assert.Equal([Spaces.Text("Today_· Mon_19_Oct"), Spaces.Text("Fri_16_Oct")], viewModel.Groups.Select(group => group.Heading));
        Assert.Equal(["JOB-1053", "JOB-1037"], viewModel.Groups[0].Select(card => card.JobNumber));
        Assert.Equal("Tue 20 Oct, 1:00–3:00 PM", viewModel.Groups[0][0].TimeWindow);
        Assert.Equal("Fri 16 Oct, 1:00–3:00 PM", viewModel.Groups[0][1].TimeWindow);
        Assert.Equal("2:00–3:00 PM", viewModel.Groups[1][0].TimeWindow);
    }

    [Fact]
    public async Task CompletedGroupsAndTheirCardsAreNewestFirstWhateverOrderTheListArrivesIn()
    {
        var earlier = CompletedToday1037();
        var later = CancelledToday1053();

        var viewModel = await CompletedAsync(new GetAssignedJobsResponse([JobDtoBuilder.Job1036Summary(), earlier, later]));

        Assert.Equal([new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 16)], viewModel.Groups.Select(group => group.Day));
        Assert.Equal([later.Id, earlier.Id], viewModel.Groups[0].Select(card => card.Id));
    }

    [Fact]
    public async Task TheActiveSegmentKeepsTheListOrderAndItsWellsNeverCarryADate()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())));
        var viewModel = harness.CreateMyJobs();

        await viewModel.AppearingCommand.ExecuteAsync(null);

        Assert.Equal(["JOB-1042", "JOB-1047", "JOB-1053"], viewModel.Groups.SelectMany(group => group).Select(card => card.JobNumber));
        Assert.Equal(["9:00–11:00 AM", "1:30–3:00 PM", "1:00–3:00 PM"], viewModel.Groups.SelectMany(group => group).Select(card => card.TimeWindow));
    }

    [Theory]
    [InlineData(16, 14, 16, "2:00–3:00 PM")]
    [InlineData(16, 23, 16, "11:00–11:30 PM")]
    [InlineData(16, 14, 19, "Fri 16 Oct, 2:00–3:00 PM")]
    [InlineData(20, 13, 19, "Tue 20 Oct, 1:00–2:00 PM")]
    [InlineData(15, 9, 16, "Thu 15 Oct, 9:00–10:00 AM")]
    public void TheWellTextCarriesTheShortDateOnlyWhenTheSlotDayDiffersFromTheGroupDay(int slotDay, int slotHour, int groupDay, string expected)
    {
        var start = JobDtoBuilder.Local(10, slotDay, slotHour, 0);
        var end = slotHour == 23 ? start.AddMinutes(30) : start.AddHours(1);
        var job = JobDtoBuilder.Assigned(JobDtoBuilder.Job1036, 1036, "Office AC filter replacement", JobPriority.Low, JobStatus.Cancelled, "Summit Coworking", "12 Summit Plaza, Floor 4", start, end, cancelledAt: JobDtoBuilder.Local(10, groupDay, 9, 20));

        var text = JobListItem.WellText(job, JobDtoBuilder.Zone, new DateOnly(2026, 10, 19), new DateOnly(2026, 10, groupDay));

        Assert.Equal(expected, text);
    }

    private static AssignedJobResponse CompletedToday1037() => JobDtoBuilder.Assigned(
        JobDtoBuilder.Job1037, 1037, "Annual boiler service", JobPriority.Normal, JobStatus.Completed,
        "Harborview Apartments", "600 Bayfront Avenue", JobDtoBuilder.Local(10, 16, 13, 0), JobDtoBuilder.Local(10, 16, 15, 0),
        completedAt: JobDtoBuilder.Local(10, 19, 9, 30));

    private static AssignedJobResponse CancelledToday1053() => JobDtoBuilder.Assigned(
        JobDtoBuilder.Job1053, 1053, "Rooftop unit preventive maintenance", JobPriority.Low, JobStatus.Cancelled,
        "Harborview Apartments", "600 Bayfront Avenue", JobDtoBuilder.Local(10, 20, 13, 0), JobDtoBuilder.Local(10, 20, 15, 0),
        cancelledAt: JobDtoBuilder.Local(10, 19, 10, 5));

    private static JobHistoryEntryResponse Cancelled(string? note, DateTimeOffset at) =>
        JobDtoBuilder.History(JobHistoryAction.Cancelled, JobStatus.OnHold, JobStatus.Cancelled, _dana, null, note, at);

    private static JobDetailsResponse CancelledWith(params JobHistoryEntryResponse[] entries)
    {
        var job = JobDtoBuilder.Details1036();
        return job with { History = [.. job.History.Take(2), .. entries] };
    }

    private static async Task<MyJobsViewModel> CompletedAsync(GetAssignedJobsResponse completed)
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
            () => JobDtoBuilder.Json(completed)));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        await viewModel.SelectViewCommand.ExecuteAsync(AssignedJobsView.Completed);
        return viewModel;
    }

    private static async Task<(JobDetailsViewModel ViewModel, JobsHarness Harness)> OpenAsync(JobDetailsResponse job, bool inPane = false)
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(job)), twoPane: inPane);
        var viewModel = harness.CreateDetails();
        viewModel.IsInPane = inPane;
        viewModel.AppearingCommand.Execute(null);
        await viewModel.ShowJobAsync(job.Id);
        return (viewModel, harness);
    }
}
