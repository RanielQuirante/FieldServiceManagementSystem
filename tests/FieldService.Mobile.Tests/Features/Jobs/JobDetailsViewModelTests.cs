using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Shared.Navigation;
using FieldService.Mobile.Shared.Screens;
using FieldService.Mobile.Tests.Shared.Testing;
using NSubstitute;

namespace FieldService.Mobile.Tests.Features.Jobs;

public sealed class JobDetailsViewModelTests
{
    [Fact]
    public async Task LoadsTheJobNamedByTheRouteAndShowsEveryDetailRow()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(JobDtoBuilder.Details1042())));
        var viewModel = harness.CreateDetails();

        viewModel.ApplyQueryAttributes(JobsHarness.JobQuery(JobDtoBuilder.Job1042));

        await WaitFor(() => viewModel.State == ScreenState.Content);
        Assert.Equal("/api/jobs/" + JobDtoBuilder.Job1042.ToString("D"), Assert.Single(harness.Stub.Requests).Path);
        Assert.Equal("JOB-1042", viewModel.JobNumber);
        Assert.Equal("Rooftop HVAC unit not cooling", viewModel.Title);
        Assert.Equal(JobStatus.InProgress, viewModel.Status);
        Assert.Equal(JobPriority.High, viewModel.Priority);
        Assert.Equal("In progress since 9:12 AM", viewModel.StatusLine);
        Assert.Equal("Brightwater Medical Clinic", viewModel.CustomerName);
        Assert.Equal("214 Harbor Road, Suite 3", viewModel.SiteAddress);
        Assert.Equal("Roof access through the east stairwell. Ask reception for the roof key.", viewModel.AccessNotes);
        Assert.Equal("Nina Alvarez", viewModel.ContactValue);
        Assert.Equal("(555) 014-6620", viewModel.ContactSecondary);
        Assert.Equal("Mon 19 Oct, 9:00–11:00 AM", viewModel.ScheduledText);
        Assert.True(viewModel.ShowsScheduledRow);
        Assert.Equal("RTU-2, 10-ton packaged rooftop unit", viewModel.Equipment);
        Assert.Equal("Rooftop HVAC service", viewModel.ChecklistName);
        Assert.StartsWith("Rooftop unit RTU-2 runs but is not cooling.", viewModel.Description, StringComparison.Ordinal);
        Assert.True(viewModel.ShowsRowActions);
        Assert.False(viewModel.ShowsDock);
        Assert.False(viewModel.IsAssigned);
    }

    [Fact]
    public async Task TheTimelineListsTheHistoryOldestFirstWithTheNewestMarked()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(JobDtoBuilder.Details1042())));
        var viewModel = harness.CreateDetails();

        await viewModel.ShowJobAsync(JobDtoBuilder.Job1042);

        Assert.Equal(
            [
                new TimelineEntry(null, "8:14 AM", "Created by Dana Whitfield", null, false),
                new TimelineEntry(null, "8:20 AM", "Assigned to Marco Reyes", null, false),
                new TimelineEntry(null, "9:12 AM", "Started by Marco Reyes", null, true),
            ],
            viewModel.Timeline);
        Assert.Equal("9:12 AM, Started by Marco Reyes", viewModel.Timeline[2].Description);
    }

    [Fact]
    public async Task EntriesFromEarlierDaysCarryTheirDateAndNotesAreQuoted()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(JobDtoBuilder.Details1036())));
        var viewModel = harness.CreateDetails();

        await viewModel.ShowJobAsync(JobDtoBuilder.Job1036);

        var cancelled = viewModel.Timeline[2];
        Assert.Equal("Fri 16 Oct", cancelled.Day);
        Assert.Equal("9:20 AM", cancelled.Time);
        Assert.Equal("Cancelled by Dana Whitfield", cancelled.Text);
        Assert.Equal("“Building management replaced the filters under its own maintenance contract.”", cancelled.QuotedNote);
        Assert.Equal("Thu 15 Oct", viewModel.Timeline[0].Day);
        Assert.Equal(Timeline.WithDayWidth, Timeline.WidthFor(viewModel.Timeline));
    }

    [Theory]
    [InlineData("Job1039", "Completed at 8:52 AM")]
    [InlineData("Job1036", "Cancelled on Fri 16 Oct at 9:20 AM")]
    [InlineData("Job1037", "On hold since Fri 16 Oct, 1:50 PM")]
    public async Task TheStatusLineFollowsTheJobState(string job, string statusLine)
    {
        var details = job switch
        {
            "Job1039" => JobDtoBuilder.Details1039(),
            "Job1036" => JobDtoBuilder.Details1036(),
            _ => JobDtoBuilder.Details1037(),
        };
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(details)));
        var viewModel = harness.CreateDetails();

        await viewModel.ShowJobAsync(details.Id);

        Assert.Equal(statusLine, viewModel.StatusLine);
        Assert.False(viewModel.ShowsDock);
        Assert.False(viewModel.IsAssigned);
    }

    [Fact]
    public async Task ACompletedJobLeavesOutTheRowsWithoutAValue()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(JobDtoBuilder.Details1039())));
        var viewModel = harness.CreateDetails();

        await viewModel.ShowJobAsync(JobDtoBuilder.Job1039);

        Assert.False(viewModel.HasAccessNotes);
        Assert.False(viewModel.HasContact);
        Assert.False(viewModel.HasEquipment);
        Assert.False(viewModel.CanCall);
        Assert.False(viewModel.ShowsRowCall);
    }

    [Fact]
    public async Task AJobThatIsNoLongerTheTechniciansReturnsToMyJobsWithTheNotice()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(ProblemResponses.NotFound));
        var viewModel = harness.CreateDetails();

        await viewModel.ShowJobAsync(JobDtoBuilder.Job1047);

        await harness.Navigator.Received(1).GoToAsync(
            Routes.Back,
            Arg.Is<IDictionary<string, object>>(query => (string)query[Routes.NoticeParameter] == Routes.JobUnassignedNotice));
        Assert.NotEqual(ScreenState.Error, viewModel.State);
    }

    [Fact]
    public async Task ShowsTheErrorWithItsReferenceAndRetryRecovers()
    {
        var correlationId = Guid.NewGuid().ToString("D");
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => ProblemResponses.ServerError(correlationId),
            () => JobDtoBuilder.Json(JobDtoBuilder.Details1042())));
        var viewModel = harness.CreateDetails();

        await viewModel.ShowJobAsync(JobDtoBuilder.Job1042);

        Assert.Equal(ScreenState.Error, viewModel.State);
        Assert.Equal(new ScreenError("The job could not be loaded.", "Check your connection, then try again.", correlationId), viewModel.Error);

        await viewModel.RetryCommand.ExecuteAsync(null);

        Assert.Equal(ScreenState.Content, viewModel.State);
        Assert.Equal(2, harness.Stub.Requests.Count);
    }

    [Fact]
    public async Task GoingOfflineKeepsTheLoadedJobUnderTheOfflineBanner()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(JobDtoBuilder.Details1042())));
        var viewModel = harness.CreateDetails();
        viewModel.AppearingCommand.Execute(null);
        await viewModel.ShowJobAsync(JobDtoBuilder.Job1042);

        harness.SetOnline(false);

        Assert.Equal(ScreenState.Offline, viewModel.State);
        Assert.True(viewModel.ShowsContent);
        Assert.Equal("Showing this job as of 10:42 AM.", viewModel.OfflineMessage);
        Assert.Equal("You're offline. Showing this job as of 10:42 AM.", JobDetailsViewModel.OfflineTitle + " " + viewModel.OfflineMessage);
        Assert.Equal("Rooftop HVAC unit not cooling", viewModel.Title);
    }

    [Fact]
    public async Task OfflineBeforeTheFirstLoadShowsTheErrorWithoutARequest()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence());
        harness.SetOnline(false);
        var viewModel = harness.CreateDetails();

        await viewModel.ShowJobAsync(JobDtoBuilder.Job1042);

        Assert.Equal(ScreenState.Error, viewModel.State);
        Assert.Equal(new ScreenError("The job could not be loaded.", "Check your connection, then try again.", null), viewModel.Error);
        Assert.Empty(harness.Stub.Requests);
    }

    [Fact]
    public async Task BackReturnsToMyJobs()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence());
        var viewModel = harness.CreateDetails();

        await viewModel.BackCommand.ExecuteAsync(null);

        await harness.Navigator.Received(1).GoToAsync(Routes.Back);
    }

    [Fact]
    public async Task AJobIdSentAsTextIsAccepted()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(JobDtoBuilder.Details1042())));
        var viewModel = harness.CreateDetails();

        viewModel.ApplyQueryAttributes(new Dictionary<string, object> { [Routes.JobIdParameter] = JobDtoBuilder.Job1042.ToString("D") });

        await WaitFor(() => viewModel.State == ScreenState.Content);
        Assert.Equal(JobDtoBuilder.Job1042, viewModel.JobId);
    }

    [Fact]
    public async Task AnUnknownStatusOrActionFromANewerApiReadsAsUnknown()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(JobDtoBuilder.Details1042(), FieldService.Mobile.Shared.Http.MobileJsonContext.Default.JobDetailsResponse)
            .Replace("\"status\":\"InProgress\"", "\"status\":\"Archived\"", StringComparison.Ordinal)
            .Replace("\"action\":\"Started\"", "\"action\":\"Escalated\"", StringComparison.Ordinal);
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Respond(json)));
        var viewModel = harness.CreateDetails();

        await viewModel.ShowJobAsync(JobDtoBuilder.Job1042);

        Assert.Equal(ScreenState.Content, viewModel.State);
        Assert.Equal(JobStatus.Unknown, viewModel.Status);
        Assert.Equal("Updated by Marco Reyes", viewModel.Timeline[2].Text);
        Assert.Equal(string.Empty, viewModel.StatusLine);
    }

    [Fact]
    public async Task AMalformedBodyShowsTheErrorWithTheRequestReference()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Respond("{\"id\":")));
        var viewModel = harness.CreateDetails();

        await viewModel.ShowJobAsync(JobDtoBuilder.Job1042);

        Assert.Equal(ScreenState.Error, viewModel.State);
        Assert.Equal(Assert.Single(harness.Stub.Requests).CorrelationId, viewModel.Error?.Reference);
    }

    [Fact]
    public async Task TheShownJobIsForgottenWhenTheSessionEnds()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(JobDtoBuilder.Details1047())));
        var viewModel = harness.CreateDetails();
        viewModel.AppearingCommand.Execute(null);
        await viewModel.ShowJobAsync(JobDtoBuilder.Job1047);
        Assert.Equal("Elm Street Bakery", viewModel.CustomerName);

        harness.EndSession();

        Assert.Null(viewModel.Job);
        Assert.False(viewModel.HasJob);
        Assert.Equal(Guid.Empty, viewModel.JobId);
        Assert.Equal(string.Empty, viewModel.CustomerName);
        Assert.Equal(string.Empty, viewModel.SiteAddress);
        Assert.Equal(string.Empty, viewModel.ContactValue);
        Assert.Empty(viewModel.Timeline);
        Assert.Null(viewModel.StartsLine);
        Assert.Null(viewModel.OfflineMessage);
        Assert.Equal(ScreenState.Loading, viewModel.State);

        harness.SetOnline(false);
        Assert.Null(viewModel.Job);
        Assert.Null(viewModel.OfflineMessage);
    }

    [Fact]
    public async Task AResponseThatArrivesAfterTheSessionEndedIsDiscarded()
    {
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var harness = new JobsHarness(new StubHttpMessageHandler((_, _) => release.Task));
        var viewModel = harness.CreateDetails();
        viewModel.AppearingCommand.Execute(null);
        var loading = viewModel.ShowJobAsync(JobDtoBuilder.Job1047);

        harness.EndSession();
        release.SetResult(JobDtoBuilder.Json(JobDtoBuilder.Details1047()));
        await loading;

        Assert.Null(viewModel.Job);
        Assert.Equal(ScreenState.Loading, viewModel.State);
        Assert.Null(viewModel.Error);
    }

    [Fact]
    public async Task APageThatIsNoLongerShownStopsListeningForTheEndOfTheSession()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(JobDtoBuilder.Details1047())));
        var viewModel = harness.CreateDetails();
        viewModel.AppearingCommand.Execute(null);
        await viewModel.ShowJobAsync(JobDtoBuilder.Job1047);

        viewModel.DisappearingCommand.Execute(null);

        harness.Session.Received(1).SessionEnded += Arg.Any<EventHandler>();
        harness.Session.Received(1).SessionEnded -= Arg.Any<EventHandler>();
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(5);
        }

        Assert.True(condition());
    }
}
