using System.Collections.Concurrent;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Features.SignIn;
using FieldService.Mobile.Shared.Formatting;
using FieldService.Mobile.Shared.Navigation;
using FieldService.Mobile.Shared.Screens;
using FieldService.Mobile.Shared.Session;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui.Devices;
using NSubstitute;

namespace FieldService.Mobile.Tests.Features.Jobs;

public sealed class MyJobsViewModelTests
{
    private const string MarcoEmail = "marco.reyes@fieldservice.example";
    private const string TomEmail = "tom.becker@fieldservice.example";

    private static readonly Guid[] _marcosJobs = [JobDtoBuilder.Job1042, JobDtoBuilder.Job1047, JobDtoBuilder.Job1053];

    [Fact]
    public async Task AppearingLoadsTheActiveViewAndGroupsTheJobsByDay()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())));
        var viewModel = harness.CreateMyJobs();

        await viewModel.AppearingCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.Stub.Requests);
        Assert.Equal("/api/technicians/me/jobs", request.Path);
        Assert.Equal("?view=active", request.Query);
        Assert.Equal(ScreenState.Content, viewModel.State);
        Assert.Equal([Spaces.Text("Today_· Mon_19_Oct"), Spaces.Text("Tomorrow_· Tue_20_Oct")], viewModel.Groups.Select(group => group.Heading));
        Assert.Equal(["JOB-1042", "JOB-1047"], viewModel.Groups[0].Select(job => job.JobNumber));
        Assert.Equal(["JOB-1053"], viewModel.Groups[1].Select(job => job.JobNumber));
        Assert.Equal(3, viewModel.ActiveCount);
        Assert.Equal("Monday 19 October", viewModel.Eyebrow);
        Assert.Equal(Spaces.Text("Updated 10:42_AM_· pull down to refresh"), viewModel.SyncText);
        Assert.Equal(SyncIndicator.Updated, viewModel.SyncIndicator);
        Assert.Equal("MR", viewModel.Initials);
    }

    [Fact]
    public async Task ShowsLoadingAndTheLoadingSyncLineWhileTheFirstRequestRuns()
    {
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var harness = new JobsHarness(new StubHttpMessageHandler((_, _) => release.Task));
        var viewModel = harness.CreateMyJobs();

        var appearing = viewModel.AppearingCommand.ExecuteAsync(null);

        Assert.Equal(ScreenState.Loading, viewModel.State);
        Assert.Equal("Loading your jobs", viewModel.SyncText);
        Assert.Equal(SyncIndicator.Loading, viewModel.SyncIndicator);
        release.SetResult(JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()));
        await appearing;
        Assert.Equal(ScreenState.Content, viewModel.State);
    }

    [Fact]
    public async Task EachCardCarriesOneDescriptionAndItsTimeWindow()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())));
        var viewModel = harness.CreateMyJobs();

        await viewModel.AppearingCommand.ExecuteAsync(null);

        var urgent = viewModel.Groups[0][1];
        Assert.Equal(
            "JOB-1047, Walk-in freezer temperature alarm, Assigned, Urgent, today 1:30 to 3:00 PM, Elm Street Bakery, 88 Elm Street",
            urgent.Description);
        Assert.Equal("1:30–3:00 PM", urgent.TimeWindow);
        Assert.True(urgent.IsUrgent);
        Assert.Equal("9:00–11:00 AM", viewModel.Groups[0][0].TimeWindow);
        Assert.Equal("1:00–3:00 PM", viewModel.Groups[1][0].TimeWindow);
    }

    [Fact]
    public async Task AnOpenJobFromAnEarlierDaySitsUnderItsOwnDayBeforeToday()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => JobDtoBuilder.Json(new GetAssignedJobsResponse([JobDtoBuilder.Job1037Summary(), JobDtoBuilder.Job1042Summary()]))));
        var viewModel = harness.CreateMyJobs();

        await viewModel.AppearingCommand.ExecuteAsync(null);

        Assert.Equal([Spaces.Text("Fri_16_Oct"), Spaces.Text("Today_· Mon_19_Oct")], viewModel.Groups.Select(group => group.Heading));
    }

    [Fact]
    public async Task TheCompletedSegmentCallsTheCompletedViewAndGroupsByTheDayTheJobLeftTheList()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoCompleted())));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);

        await viewModel.SelectViewCommand.ExecuteAsync(AssignedJobsView.Completed);

        Assert.Equal("?view=completed", harness.Stub.Requests[1].Query);
        Assert.True(viewModel.IsCompletedSelected);
        Assert.Equal(["Fri 16 Oct"], viewModel.Groups.Select(group => group.Heading));
        Assert.Equal(JobStatus.Cancelled, viewModel.Groups[0][0].Status);
        Assert.Equal(3, viewModel.ActiveCount);
    }

    [Fact]
    public async Task SegmentDescriptionsCarryTheCountAndTheState()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoCompleted())));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);

        Assert.Equal("Active, 3, selected", viewModel.ActiveSegmentDescription);
        Assert.Equal("Completed, not selected", viewModel.CompletedSegmentDescription);

        await viewModel.SelectViewCommand.ExecuteAsync(AssignedJobsView.Completed);

        Assert.Equal("Active, 3, not selected", viewModel.ActiveSegmentDescription);
        Assert.Equal("Completed, selected", viewModel.CompletedSegmentDescription);
    }

    [Theory]
    [InlineData(AssignedJobsView.Active, "No jobs assigned", "New assignments appear here as soon as dispatch sends them.")]
    [InlineData(AssignedJobsView.Completed, "No completed jobs", "Jobs you submit appear here, along with any job dispatch cancels.")]
    public async Task ShowsTheEmptyStateOfEachSegment(AssignedJobsView view, string title, string message)
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => JobDtoBuilder.Json(new GetAssignedJobsResponse([])),
            () => JobDtoBuilder.Json(new GetAssignedJobsResponse([]))));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);

        await viewModel.SelectViewCommand.ExecuteAsync(view);

        Assert.Equal(ScreenState.Empty, viewModel.State);
        Assert.Empty(viewModel.Groups);
        Assert.Equal(title, viewModel.EmptyTitle);
        Assert.Equal(message, viewModel.EmptyMessage);
    }

    [Fact]
    public async Task ShowsTheErrorWithItsReferenceAndRetryRecovers()
    {
        var correlationId = Guid.NewGuid().ToString("D");
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => ProblemResponses.ServerError(correlationId),
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())));
        var viewModel = harness.CreateMyJobs();

        await viewModel.AppearingCommand.ExecuteAsync(null);

        Assert.Equal(ScreenState.Error, viewModel.State);
        Assert.Equal(new ScreenError("Jobs could not be loaded.", "Check your connection, then try again.", correlationId), viewModel.Error);
        Assert.Empty(viewModel.Groups);

        await viewModel.RetryCommand.ExecuteAsync(null);

        Assert.Equal(ScreenState.Content, viewModel.State);
        Assert.Null(viewModel.Error);
        Assert.Equal(3, viewModel.Groups.Sum(group => group.Count));
    }

    [Fact]
    public async Task ShowsTheRetryWait_WhenTheListIsRateLimited()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => ProblemResponses.RateLimited(TimeSpan.FromSeconds(30))));
        var viewModel = harness.CreateMyJobs();

        await viewModel.AppearingCommand.ExecuteAsync(null);

        Assert.Equal("Try again in 30 seconds.", viewModel.Error?.Message);
        Assert.True(viewModel.Error?.HasReference);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnUnreachableApiShowsTheErrorWithoutAReferenceAndNeverThrows(bool timeout)
    {
        Exception failure = timeout ? new TaskCanceledException("timeout") : new HttpRequestException("Connection refused");
        var harness = new JobsHarness(StubHttpMessageHandler.Throwing(failure));
        var viewModel = harness.CreateMyJobs();

        await viewModel.AppearingCommand.ExecuteAsync(null);

        Assert.Equal(ScreenState.Error, viewModel.State);
        Assert.Equal(new ScreenError("Jobs could not be loaded.", "Check your connection, then try again.", null), viewModel.Error);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task GoingOfflineKeepsTheJobsFromThisSessionUnderTheOfflineBanner()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        harness.Clock.Advance(TimeSpan.FromMinutes(11));

        harness.SetOnline(false);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(ScreenState.Offline, viewModel.State);
        Assert.True(viewModel.ShowsContent);
        Assert.Equal(3, viewModel.Groups.Sum(group => group.Count));
        Assert.Equal("Showing jobs from 10:42 AM.", viewModel.OfflineMessage);
        Assert.Equal("Last updated 10:42 AM", viewModel.SyncText);
        Assert.Equal(SyncIndicator.Offline, viewModel.SyncIndicator);
        Assert.Single(harness.Stub.Requests);
    }

    [Fact]
    public async Task OfflineBeforeAnyLoadShowsTheErrorWithoutARequest()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence());
        harness.SetOnline(false);
        var viewModel = harness.CreateMyJobs();

        await viewModel.AppearingCommand.ExecuteAsync(null);

        Assert.Equal(ScreenState.Error, viewModel.State);
        Assert.Equal(new ScreenError("Jobs could not be loaded.", "Check your connection, then try again.", null), viewModel.Error);
        Assert.Empty(harness.Stub.Requests);
    }

    [Fact]
    public async Task ReloadsWhenTheConnectionComesBack()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
            () => JobDtoBuilder.Json(new GetAssignedJobsResponse([JobDtoBuilder.Job1047Summary()]))));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        harness.SetOnline(false);
        Assert.Equal(ScreenState.Offline, viewModel.State);

        harness.SetOnline(true);

        await WaitFor(() => viewModel.State == ScreenState.Content && viewModel.ActiveCount == 1);
        Assert.Equal(2, harness.Stub.Requests.Count);
        Assert.Null(viewModel.OfflineMessage);
    }

    [Fact]
    public async Task PullToRefreshReloadsAndEndsTheRefreshIndicator()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
            () => JobDtoBuilder.Json(new GetAssignedJobsResponse([JobDtoBuilder.Job1042Summary()]))));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);

        viewModel.IsRefreshing = true;
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.Stub.Requests.Count);
        Assert.False(viewModel.IsRefreshing);
        Assert.Equal(1, viewModel.ActiveCount);
    }

    [Fact]
    public async Task ReloadsEachTimeThePageAppears()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())));
        var viewModel = harness.CreateMyJobs();

        await viewModel.AppearingCommand.ExecuteAsync(null);
        viewModel.DisappearingCommand.Execute(null);
        await viewModel.AppearingCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.Stub.Requests.Count);
        Assert.Equal(ScreenState.Content, viewModel.State);
    }

    [Fact]
    public async Task OpeningAJobOnAPhoneNavigatesToJobDetailsAndClearsTheSelection()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        var job = viewModel.Groups[0][1];

        viewModel.SelectedJob = job;
        await viewModel.OpenJobCommand.ExecuteAsync(job);

        await harness.Navigator.Received(1).GoToAsync(
            Routes.JobDetails,
            Arg.Is<IDictionary<string, object>>(query => (Guid)query[Routes.JobIdParameter] == JobDtoBuilder.Job1047));
        Assert.Null(viewModel.SelectedJob);
    }

    [Fact]
    public async Task OpeningAJobOnTheTwoPaneLayoutShowsItBesideTheListWithoutNavigating()
    {
        var harness = new JobsHarness(
            StubHttpMessageHandler.Sequence(
                () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
                () => JobDtoBuilder.Json(JobDtoBuilder.Details1042())),
            twoPane: true);
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        var job = viewModel.Groups[0][0];

        viewModel.SelectedJob = job;
        await viewModel.OpenJobCommand.ExecuteAsync(job);

        Assert.True(viewModel.IsTwoPane);
        Assert.Same(job, viewModel.SelectedJob);
        Assert.True(viewModel.Details.IsInPane);
        Assert.Equal(JobDtoBuilder.Job1042, viewModel.Details.JobId);
        Assert.Equal("Rooftop HVAC unit not cooling", viewModel.Details.Title);
        Assert.False(viewModel.Details.ShowsDock);
        Assert.Equal("/api/jobs/" + JobDtoBuilder.Job1042.ToString("D"), harness.Stub.Requests[1].Path);
        await harness.Navigator.DidNotReceive().GoToAsync(Arg.Any<string>(), Arg.Any<IDictionary<string, object>>());
    }

    [Fact]
    public async Task APaneJobThatIsNoLongerAssignedClearsTheSelectionAndShowsTheNotice()
    {
        var harness = new JobsHarness(
            StubHttpMessageHandler.Sequence(
                () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
                ProblemResponses.NotFound,
                () => JobDtoBuilder.Json(new GetAssignedJobsResponse([JobDtoBuilder.Job1042Summary()]))),
            twoPane: true);
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        var job = viewModel.Groups[0][1];
        viewModel.SelectedJob = job;

        await viewModel.OpenJobCommand.ExecuteAsync(job);

        await WaitFor(() => viewModel.ActiveCount == 1);
        Assert.Null(viewModel.SelectedJob);
        Assert.Equal("This job is no longer assigned to you.", viewModel.Notice);
    }

    [Fact]
    public async Task TheNoLongerAssignedNoticeArrivesWithTheBackNavigationAndARefreshClearsIt()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())));
        var viewModel = harness.CreateMyJobs();

        viewModel.ApplyQueryAttributes(new Dictionary<string, object> { [Routes.NoticeParameter] = Routes.JobUnassignedNotice });

        Assert.True(viewModel.HasNotice);
        Assert.Equal("This job is no longer assigned to you.", viewModel.Notice);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.False(viewModel.HasNotice);
    }

    [Fact]
    public async Task SigningOutFromTheAccountSheetEndsTheSessionAndForgetsTheJobs()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        viewModel.OpenAccountSheetCommand.Execute(null);
        Assert.True(viewModel.IsAccountSheetOpen);
        Assert.Equal("Marco Reyes", viewModel.DisplayName);
        Assert.Equal("marco.reyes@fieldservice.example", viewModel.Email);

        await viewModel.SignOutCommand.ExecuteAsync(null);

        await harness.Session.Received(1).SignOutAsync(Arg.Any<CancellationToken>());
        Assert.False(viewModel.IsAccountSheetOpen);
        Assert.Equal(0, viewModel.AccountFocusRequest);
        Assert.Empty(viewModel.Groups);
        Assert.Null(viewModel.ActiveCount);
        Assert.False(viewModel.IsSigningOut);
    }

    [Fact]
    public void CancelTheOverlayAndBackCloseTheAccountSheetAndReturnFocusToTheAccountButton()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence());
        var viewModel = harness.CreateMyJobs();

        viewModel.OpenAccountSheetCommand.Execute(null);
        viewModel.CloseAccountSheetCommand.Execute(null);

        Assert.False(viewModel.IsAccountSheetOpen);
        Assert.Equal(1, viewModel.AccountFocusRequest);
        viewModel.CloseAccountSheetCommand.Execute(null);
        Assert.Equal(1, viewModel.AccountFocusRequest);

        viewModel.OpenAccountSheetCommand.Execute(null);
        Assert.True(viewModel.CloseSheet());

        Assert.False(viewModel.IsAccountSheetOpen);
        Assert.Equal(2, viewModel.AccountFocusRequest);
        Assert.False(viewModel.CloseSheet());
        Assert.Equal(2, viewModel.AccountFocusRequest);
    }

    [Fact]
    public void AndroidBackClosesAnOpenSheetBeforeItNavigates()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence());
        var viewModel = harness.CreateMyJobs();
        viewModel.OpenAccountSheetCommand.Execute(null);

        Assert.True(viewModel.CloseSheet());
        Assert.False(viewModel.IsAccountSheetOpen);
        Assert.False(viewModel.CloseSheet());
    }

    [Theory]
    [InlineData("Desktop", 900, true)]
    [InlineData("Desktop", 1280, true)]
    [InlineData("Desktop", 899, false)]
    [InlineData("Phone", 1200, false)]
    [InlineData("Tablet", 1200, false)]
    public void TheTwoPaneLayoutNeedsTheDesktopIdiomAndAtLeast900Units(string idiom, double width, bool twoPane)
    {
        var deviceIdiom = DeviceIdiom.Create(idiom);

        Assert.Equal(twoPane, WindowLayout.IsTwoPaneFor(deviceIdiom, width));
    }

    [Fact]
    public async Task ListLogsHoldNoCustomerOrSiteData()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
            () => ProblemResponses.ServerError(Guid.NewGuid().ToString("D"))));
        var viewModel = harness.CreateMyJobs();

        await viewModel.AppearingCommand.ExecuteAsync(null);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        var text = harness.Logs.AllText();
        Assert.Contains("jobs.load-failed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Elm Street", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Marco", text, StringComparison.Ordinal);
        Assert.DoesNotContain("view=active", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSyncLineIsEmptyAfterAFailedFirstLoadAndNeverClaimsAnUpdate()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Throwing(new HttpRequestException("Connection refused")));
        var viewModel = harness.CreateMyJobs();

        Assert.Equal(string.Empty, viewModel.SyncText);

        await viewModel.AppearingCommand.ExecuteAsync(null);

        Assert.Equal(ScreenState.Error, viewModel.State);
        Assert.Equal(string.Empty, viewModel.SyncText);
        Assert.Null(viewModel.LastUpdated);
    }

    [Fact]
    public async Task AfterAnExpirySigningInAsAnotherTechnicianWhileOfflineShowsNoJobOfTheFirst()
    {
        var (router, session, harness, viewModel) = await MarcoOnMyJobsAsync();
        await ExpireWhileJobDetailsIsShownAsync(router, session, harness, viewModel);

        await session.Session.SignInAsync(TomEmail, TestSession.NewPassword(), CancellationToken.None);
        harness.SetOnline(false);
        await viewModel.AppearingCommand.ExecuteAsync(null);

        Assert.Equal(ScreenState.Error, viewModel.State);
        Assert.Empty(viewModel.Groups);
        Assert.Null(viewModel.ActiveCount);
        Assert.Null(viewModel.LastUpdated);
        Assert.Null(viewModel.OfflineMessage);
        Assert.Equal(string.Empty, viewModel.SyncText);
        Assert.Equal("Tom Becker", viewModel.DisplayName);
        Assert.Equal("TB", viewModel.Initials);
        AssertShowsNoJobOf(viewModel, _marcosJobs);
    }

    [Fact]
    public async Task AfterAnExpiryTheNextTechnicianSeesNoJobOfTheFirstWhileTheirFirstRequestIsPending()
    {
        var (router, session, harness, viewModel) = await MarcoOnMyJobsAsync();
        await ExpireWhileJobDetailsIsShownAsync(router, session, harness, viewModel);
        await session.Session.SignInAsync(TomEmail, TestSession.NewPassword(), CancellationToken.None);
        var tomsJobs = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        router.TomHold = tomsJobs;

        var appearing = viewModel.AppearingCommand.ExecuteAsync(null);
        await WaitFor(() => router.HeldRequests == 1);

        Assert.Equal(ScreenState.Loading, viewModel.State);
        Assert.Empty(viewModel.Groups);
        Assert.Null(viewModel.ActiveCount);
        Assert.Equal("Loading your jobs", viewModel.SyncText);
        AssertShowsNoJobOf(viewModel, _marcosJobs);

        harness.SetOnline(false);

        Assert.Empty(viewModel.Groups);
        Assert.Null(viewModel.OfflineMessage);
        AssertShowsNoJobOf(viewModel, _marcosJobs);

        tomsJobs.SetResult(JobDtoBuilder.Json(new GetAssignedJobsResponse([JobDtoBuilder.Job1037Summary()])));
        await appearing;

        Assert.Equal([JobDtoBuilder.Job1037], viewModel.Groups.SelectMany(group => group).Select(job => job.Id));
        Assert.Equal(1, viewModel.ActiveCount);
        AssertShowsNoJobOf(viewModel, _marcosJobs);
    }

    [Fact]
    public async Task TheTwoPaneSelectionIsClearedWhenTheSessionEnds()
    {
        var harness = new JobsHarness(
            StubHttpMessageHandler.Sequence(
                () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
                () => JobDtoBuilder.Json(JobDtoBuilder.Details1042())),
            twoPane: true);
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        var job = viewModel.Groups[0][0];
        viewModel.SelectedJob = job;
        await viewModel.OpenJobCommand.ExecuteAsync(job);
        Assert.Equal("Brightwater Medical Clinic", viewModel.Details.CustomerName);

        harness.EndSession();

        Assert.Null(viewModel.SelectedJob);
        Assert.False(viewModel.HasSelection);
        Assert.True(viewModel.ShowsNoSelection);
        Assert.Null(viewModel.Details.Job);
        Assert.Equal(Guid.Empty, viewModel.Details.JobId);
        Assert.Equal(string.Empty, viewModel.Details.CustomerName);
        Assert.Empty(viewModel.Details.Timeline);
        Assert.Equal(ScreenState.Loading, viewModel.Details.State);
        Assert.Empty(viewModel.Groups);
        AssertShowsNoJobOf(viewModel, _marcosJobs);
    }

    [Fact]
    public async Task EveryPerUserValueIsClearedWhenTheSessionEnds()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoCompleted())));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        await viewModel.SelectViewCommand.ExecuteAsync(AssignedJobsView.Completed);
        viewModel.ApplyQueryAttributes(new Dictionary<string, object> { [Routes.NoticeParameter] = Routes.JobUnassignedNotice });
        harness.SetOnline(false);
        viewModel.OpenAccountSheetCommand.Execute(null);
        Assert.Equal(ScreenState.Offline, viewModel.State);
        harness.Session.User.Returns((SignedInUser?)null);

        harness.EndSession();

        Assert.Empty(viewModel.Groups);
        Assert.Null(viewModel.ActiveCount);
        Assert.Equal(string.Empty, viewModel.ActiveCountText);
        Assert.Null(viewModel.LastUpdated);
        Assert.Null(viewModel.OfflineMessage);
        Assert.Null(viewModel.Notice);
        Assert.Null(viewModel.Error);
        Assert.Null(viewModel.SelectedJob);
        Assert.False(viewModel.IsAccountSheetOpen);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.IsActiveSelected);
        Assert.Equal(ScreenState.Loading, viewModel.State);
        Assert.Equal(string.Empty, viewModel.SyncText);
        Assert.Equal(string.Empty, viewModel.DisplayName);
        Assert.Null(viewModel.Email);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(ScreenState.Error, viewModel.State);
        Assert.Empty(viewModel.Groups);
        Assert.Null(viewModel.OfflineMessage);
    }

    [Fact]
    public async Task AResponseForTheFirstTechnicianThatArrivesAfterTheSessionEndedIsDiscarded()
    {
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var harness = new JobsHarness(new StubHttpMessageHandler((_, _) => release.Task));
        var viewModel = harness.CreateMyJobs();
        var appearing = viewModel.AppearingCommand.ExecuteAsync(null);

        harness.EndSession();
        release.SetResult(JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()));
        await appearing;

        Assert.Empty(viewModel.Groups);
        Assert.Null(viewModel.ActiveCount);
        Assert.Null(viewModel.LastUpdated);
        Assert.Equal(ScreenState.Loading, viewModel.State);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task AppearingForAnotherTechnicianForgetsTheFirstEvenWithoutTheEndOfSessionSignal()
    {
        var tomsJobs = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var harness = new JobsHarness(new StubHttpMessageHandler((_, _) =>
            Interlocked.Increment(ref calls) == 1 ? Task.FromResult(JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())) : tomsJobs.Task));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        viewModel.DisappearingCommand.Execute(null);
        harness.Session.User.Returns(new SignedInUser(JobDtoBuilder.Tom, "Tom Becker", UserRole.Technician));

        var appearing = viewModel.AppearingCommand.ExecuteAsync(null);

        Assert.Equal(ScreenState.Loading, viewModel.State);
        Assert.Empty(viewModel.Groups);
        Assert.Null(viewModel.ActiveCount);
        AssertShowsNoJobOf(viewModel, _marcosJobs);
        tomsJobs.SetResult(JobDtoBuilder.Json(new GetAssignedJobsResponse([JobDtoBuilder.Job1037Summary()])));
        await appearing;
        Assert.Equal(1, viewModel.ActiveCount);
    }

    [Fact]
    public async Task AnAnswerForTheTechnicianWhoAskedIsDiscardedWhenAnotherIsSignedInByTheTimeItArrives()
    {
        var marcosJobs = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var harness = new JobsHarness(new StubHttpMessageHandler((_, _) =>
            Interlocked.Increment(ref calls) == 1
                ? marcosJobs.Task
                : Task.FromResult(JobDtoBuilder.Json(new GetAssignedJobsResponse([JobDtoBuilder.Job1037Summary()])))));
        var viewModel = harness.CreateMyJobs();
        var appearing = viewModel.AppearingCommand.ExecuteAsync(null);

        harness.Session.User.Returns(new SignedInUser(JobDtoBuilder.Tom, "Tom Becker", UserRole.Technician));
        marcosJobs.SetResult(JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()));
        await appearing;
        await WaitFor(() => viewModel.ActiveCount == 1);

        Assert.Equal(2, harness.Stub.Requests.Count);
        Assert.Equal([JobDtoBuilder.Job1037], viewModel.Groups.SelectMany(group => group).Select(job => job.Id));
        AssertShowsNoJobOf(viewModel, _marcosJobs);
        Assert.False(viewModel.IsBusy);
        Assert.Contains("jobs.user-changed", harness.Logs.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAnswerThatArrivesAfterTheSignedInTechnicianIsGoneIsDiscarded()
    {
        var marcosJobs = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var harness = new JobsHarness(new StubHttpMessageHandler((_, _) => marcosJobs.Task));
        var viewModel = harness.CreateMyJobs();
        var appearing = viewModel.AppearingCommand.ExecuteAsync(null);

        harness.Session.User.Returns((SignedInUser?)null);
        marcosJobs.SetResult(JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()));
        await appearing;

        Assert.Single(harness.Stub.Requests);
        Assert.Empty(viewModel.Groups);
        Assert.Null(viewModel.ActiveCount);
        Assert.Equal(ScreenState.Loading, viewModel.State);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task AFirstLoadStartedBeforeTheSessionWasRestoredKeepsItsAnswer()
    {
        JobsHarness? harness = null;
        harness = new JobsHarness(new StubHttpMessageHandler((_, _) =>
        {
            harness!.Session.User.Returns(new SignedInUser(JobDtoBuilder.Marco, "Marco Reyes", UserRole.Technician));
            return Task.FromResult(JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()));
        }));
        harness.Session.User.Returns((SignedInUser?)null);
        var viewModel = harness.CreateMyJobs();

        await viewModel.AppearingCommand.ExecuteAsync(null);

        Assert.Single(harness.Stub.Requests);
        Assert.Equal(ScreenState.Content, viewModel.State);
        Assert.Equal(3, viewModel.ActiveCount);
    }

    [Fact]
    public async Task TheSameTechnicianReturningToMyJobsKeepsTheListWhileItReloads()
    {
        var reload = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var harness = new JobsHarness(new StubHttpMessageHandler((_, _) =>
            Interlocked.Increment(ref calls) == 1 ? Task.FromResult(JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())) : reload.Task));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        viewModel.DisappearingCommand.Execute(null);

        var appearing = viewModel.AppearingCommand.ExecuteAsync(null);

        Assert.Equal(ScreenState.Content, viewModel.State);
        Assert.Equal(3, viewModel.Groups.Sum(group => group.Count));
        reload.SetResult(JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()));
        await appearing;
    }

    [Fact]
    public async Task TheEndOfASessionSignalledFromAnotherThreadIsAppliedOnTheScreensThread()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())));
        var screen = new QueuedSynchronizationContext();
        var viewModel = QueuedSynchronizationContext.CreateOn(screen, harness.CreateMyJobs);

        await viewModel.AppearingCommand.ExecuteAsync(null);

        await Task.Run(harness.EndSession, TestContext.Current.CancellationToken);

        Assert.Equal(3, viewModel.Groups.Sum(group => group.Count));
        Assert.Equal(1, screen.RunPending());
        Assert.Empty(viewModel.Groups);
        Assert.Null(viewModel.ActiveCount);
    }

    [Fact]
    public async Task AFailedReloadWhileOnlineKeepsTheLoadedJobsUnderTheRefreshBanner()
    {
        var correlationId = Guid.NewGuid().ToString("D");
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
            () => ProblemResponses.ServerError(correlationId)));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        harness.Clock.Advance(TimeSpan.FromMinutes(23));

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(ScreenState.Content, viewModel.State);
        Assert.True(viewModel.ShowsContent);
        Assert.Equal(_marcosJobs, viewModel.Groups.SelectMany(group => group).Select(job => job.Id));
        Assert.True(viewModel.HasRefreshError);
        Assert.Equal(new ScreenError("Jobs could not be refreshed.", "Showing jobs from " + At("10:42 AM") + ".", correlationId), viewModel.RefreshError);
        Assert.False(viewModel.IsError);
        Assert.Null(viewModel.Error);
        Assert.Equal(Updated("10:42 AM"), viewModel.SyncText);
        Assert.Equal("Updated " + At("10:42 AM"), viewModel.UpdatedText);
        Assert.Equal(3, viewModel.ActiveCount);
        Assert.Equal("Active, 3, selected", viewModel.ActiveSegmentDescription);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.IsRefreshing);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AReloadThatNeverReachesTheApiKeepsTheJobsWithoutAReference(bool timeout)
    {
        Exception failure = timeout ? new TaskCanceledException("timeout") : new HttpRequestException("Connection refused");
        var calls = 0;
        var harness = new JobsHarness(new StubHttpMessageHandler((_, _) => Interlocked.Increment(ref calls) == 1
            ? Task.FromResult(JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()))
            : Task.FromException<HttpResponseMessage>(failure)));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(ScreenState.Content, viewModel.State);
        Assert.Equal(3, viewModel.Groups.Sum(group => group.Count));
        Assert.Equal(new ScreenError("Jobs could not be refreshed.", "Showing jobs from " + At("10:42 AM") + ".", null), viewModel.RefreshError);
    }

    [Fact]
    public async Task TryAgainAfterAFailedReloadShowsTheNewJobsAndRemovesTheBanner()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
            () => ProblemResponses.ServerError(Guid.NewGuid().ToString("D")),
            () => JobDtoBuilder.Json(new GetAssignedJobsResponse([JobDtoBuilder.Job1047Summary()]))));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        harness.Clock.Advance(TimeSpan.FromMinutes(5));
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasRefreshError);
        harness.Clock.Advance(TimeSpan.FromMinutes(5));

        await viewModel.RetryCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasRefreshError);
        Assert.Null(viewModel.RefreshError);
        Assert.Equal(ScreenState.Content, viewModel.State);
        Assert.Equal([JobDtoBuilder.Job1047], viewModel.Groups.SelectMany(group => group).Select(job => job.Id));
        Assert.Equal(1, viewModel.ActiveCount);
        Assert.Equal(Updated("10:52 AM"), viewModel.SyncText);
    }

    [Fact]
    public async Task AFailedReloadOnAppearingKeepsTheJobsToo()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
            () => ProblemResponses.ServerError(Guid.NewGuid().ToString("D"))));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        viewModel.DisappearingCommand.Execute(null);

        await viewModel.AppearingCommand.ExecuteAsync(null);

        Assert.Equal(ScreenState.Content, viewModel.State);
        Assert.Equal(3, viewModel.Groups.Sum(group => group.Count));
        Assert.True(viewModel.HasRefreshError);
        Assert.Equal(3, viewModel.ActiveCount);
    }

    [Fact]
    public async Task AFailedReloadOfAnEmptyListKeepsTheEmptyStateUnderTheBanner()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => JobDtoBuilder.Json(new GetAssignedJobsResponse([])),
            () => ProblemResponses.ServerError(Guid.NewGuid().ToString("D"))));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(ScreenState.Empty, viewModel.State);
        Assert.True(viewModel.HasRefreshError);
        Assert.Equal("No jobs assigned", viewModel.EmptyTitle);
        Assert.Equal(0, viewModel.ActiveCount);
        Assert.Equal(Updated("10:42 AM"), viewModel.SyncText);
    }

    [Fact]
    public async Task AFailedFirstLoadOfTheOtherSegmentReplacesItsListAndClaimsNoUpdate()
    {
        var correlationId = Guid.NewGuid().ToString("D");
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
            () => ProblemResponses.ServerError(correlationId)));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        harness.Clock.Advance(TimeSpan.FromMinutes(5));

        await viewModel.SelectViewCommand.ExecuteAsync(AssignedJobsView.Completed);

        Assert.Equal(ScreenState.Error, viewModel.State);
        Assert.Equal(new ScreenError("Jobs could not be loaded.", "Check your connection, then try again.", correlationId), viewModel.Error);
        Assert.False(viewModel.HasRefreshError);
        Assert.Null(viewModel.RefreshError);
        Assert.Empty(viewModel.Groups);
        Assert.Null(viewModel.LastUpdated);
        Assert.Equal(string.Empty, viewModel.SyncText);
        Assert.Equal(string.Empty, viewModel.UpdatedText);
        Assert.Equal(3, viewModel.ActiveCount);
    }

    [Fact]
    public async Task TheBannerTimeAndTheSyncLineNameTheLoadOfTheSegmentShown()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoCompleted()),
            () => ProblemResponses.ServerError(Guid.NewGuid().ToString("D")),
            () => ProblemResponses.ServerError(Guid.NewGuid().ToString("D"))));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        harness.Clock.Advance(TimeSpan.FromMinutes(5));
        await viewModel.SelectViewCommand.ExecuteAsync(AssignedJobsView.Completed);
        harness.Clock.Advance(TimeSpan.FromMinutes(5));

        await viewModel.SelectViewCommand.ExecuteAsync(AssignedJobsView.Active);

        Assert.Equal(ScreenState.Content, viewModel.State);
        Assert.Equal(_marcosJobs, viewModel.Groups.SelectMany(group => group).Select(job => job.Id));
        Assert.Equal("Showing jobs from " + At("10:42 AM") + ".", viewModel.RefreshError?.Message);
        Assert.Equal(Updated("10:42 AM"), viewModel.SyncText);
        Assert.Equal(3, viewModel.ActiveCount);

        await viewModel.SelectViewCommand.ExecuteAsync(AssignedJobsView.Completed);

        Assert.Equal(ScreenState.Content, viewModel.State);
        Assert.Equal(["Fri 16 Oct"], viewModel.Groups.Select(group => group.Heading.Replace(DisplayFormats.NoBreakSpace, ' ')));
        Assert.Equal("Showing jobs from " + At("10:47 AM") + ".", viewModel.RefreshError?.Message);
        Assert.Equal(Updated("10:47 AM"), viewModel.SyncText);
        Assert.Equal(3, viewModel.ActiveCount);
    }

    [Fact]
    public async Task GoingOfflineReplacesTheRefreshBannerAndAFailedReloadOnReturnBringsItBack()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
            () => ProblemResponses.ServerError(Guid.NewGuid().ToString("D")),
            () => ProblemResponses.ServerError(Guid.NewGuid().ToString("D"))));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        harness.SetOnline(false);

        Assert.Equal(ScreenState.Offline, viewModel.State);
        Assert.False(viewModel.HasRefreshError);
        Assert.Null(viewModel.RefreshError);
        Assert.Equal("Showing jobs from " + At("10:42 AM") + ".", viewModel.OfflineMessage);
        Assert.Equal(3, viewModel.Groups.Sum(group => group.Count));

        harness.SetOnline(true);

        await WaitFor(() => viewModel.HasRefreshError && !viewModel.IsBusy);
        Assert.Equal(ScreenState.Content, viewModel.State);
        Assert.Equal(3, harness.Stub.Requests.Count);
        Assert.Equal(3, viewModel.Groups.Sum(group => group.Count));
    }

    [Fact]
    public async Task TheRefreshBannerIsForgottenWhenTheSessionEnds()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
            () => ProblemResponses.ServerError(Guid.NewGuid().ToString("D"))));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasRefreshError);

        harness.EndSession();

        Assert.Null(viewModel.RefreshError);
        Assert.False(viewModel.HasRefreshError);
        Assert.Empty(viewModel.Groups);
        Assert.Equal(string.Empty, viewModel.SyncText);
    }

    [Fact]
    public async Task OnTheTwoPaneLayoutAFailedReloadKeepsTheListAndTheSelectedJob()
    {
        var harness = new JobsHarness(
            StubHttpMessageHandler.Sequence(
                () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
                () => JobDtoBuilder.Json(JobDtoBuilder.Details1042()),
                () => ProblemResponses.ServerError(Guid.NewGuid().ToString("D"))),
            twoPane: true);
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        var job = viewModel.Groups[0][0];
        viewModel.SelectedJob = job;
        await viewModel.OpenJobCommand.ExecuteAsync(job);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasRefreshError);
        Assert.Equal(job.Id, viewModel.SelectedJob?.Id);
        Assert.True(viewModel.HasSelection);
        Assert.Equal("Rooftop HVAC unit not cooling", viewModel.Details.Title);
        Assert.Equal("Updated " + At("10:42 AM"), viewModel.UpdatedText);
    }

    private static string At(string time) => time.Replace(' ', DisplayFormats.NoBreakSpace);

    private static string Updated(string time) => "Updated " + At(time) + DisplayFormats.Separator + "pull down to refresh";

    private static async Task<(TwoTechnicianRouter Router, TestSession Session, JobsHarness Harness, MyJobsViewModel ViewModel)> MarcoOnMyJobsAsync()
    {
        var router = new TwoTechnicianRouter();
        var session = TestSession.Create(new StubHttpMessageHandler(router.RespondAsync));
        var harness = new JobsHarness(session.Stub, session: session.Session);
        await session.Session.SignInAsync(MarcoEmail, TestSession.NewPassword(), CancellationToken.None);
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        Assert.Equal(3, viewModel.ActiveCount);
        Assert.Equal(ScreenState.Content, viewModel.State);
        return (router, session, harness, viewModel);
    }

    private static async Task ExpireWhileJobDetailsIsShownAsync(TwoTechnicianRouter router, TestSession session, JobsHarness harness, MyJobsViewModel viewModel)
    {
        viewModel.DisappearingCommand.Execute(null);
        var details = harness.CreateDetails();
        details.AppearingCommand.Execute(null);
        router.Expire();

        await details.ShowJobAsync(JobDtoBuilder.Job1047);

        Assert.Equal(SessionEndReason.Expired, session.Session.EndReason);
        Assert.Null(session.Session.User);
        await session.Navigator.Received(1).GoToAsync(Routes.SignIn);
        Assert.Null(details.Job);
        details.DisappearingCommand.Execute(null);
    }

    private static void AssertShowsNoJobOf(MyJobsViewModel viewModel, IReadOnlyCollection<Guid> jobIds)
    {
        Assert.DoesNotContain(viewModel.Groups.SelectMany(group => group), job => jobIds.Contains(job.Id));
        Assert.False(viewModel.SelectedJob is { } selected && jobIds.Contains(selected.Id));
        Assert.False(viewModel.Details.Job is { } shown && jobIds.Contains(shown.Id));
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(5);
        }

        Assert.True(condition());
    }

    private sealed class TwoTechnicianRouter
    {
        private readonly ConcurrentDictionary<string, Guid> _validTokens = new(StringComparer.Ordinal);
        private int _heldRequests;

        public TaskCompletionSource<HttpResponseMessage>? TomHold { get; set; }

        public int HeldRequests => Volatile.Read(ref _heldRequests);

        public void Expire() => _validTokens.Clear();

        public async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/auth/sign-in")
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                var isTom = body.Contains(TomEmail, StringComparison.Ordinal);
                var technician = isTom ? JobDtoBuilder.Tom : JobDtoBuilder.Marco;
                var accessToken = TestSession.NewAccessToken();
                _validTokens[accessToken] = technician;
                return ProblemResponses.Session(accessToken, TestSession.NewRefreshToken(), technician, isTom ? "Tom Becker" : "Marco Reyes");
            }

            if (path == "/api/auth/refresh")
            {
                return ProblemResponses.SessionExpired();
            }

            var bearer = request.Headers.Authorization?.Parameter;
            if (bearer is null || !_validTokens.TryGetValue(bearer, out var signedIn))
            {
                return ProblemResponses.Unauthenticated();
            }

            if (path.StartsWith("/api/jobs/", StringComparison.Ordinal))
            {
                return JobDtoBuilder.Json(JobDtoBuilder.Details1047());
            }

            if (signedIn == JobDtoBuilder.Marco)
            {
                return JobDtoBuilder.Json(JobDtoBuilder.MarcoActive());
            }

            if (TomHold is { } hold)
            {
                Interlocked.Increment(ref _heldRequests);
                return await hold.Task;
            }

            return JobDtoBuilder.Json(new GetAssignedJobsResponse([JobDtoBuilder.Job1037Summary()]));
        }
    }
}
