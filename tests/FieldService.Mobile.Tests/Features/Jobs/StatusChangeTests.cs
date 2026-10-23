using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Shared.Navigation;
using FieldService.Mobile.Shared.Screens;
using FieldService.Mobile.Shared.Session;
using FieldService.Mobile.Tests.Shared.Testing;
using NSubstitute;

namespace FieldService.Mobile.Tests.Features.Jobs;

public sealed class StatusChangeTests
{
    private const string MarcoEmail = "marco.reyes@fieldservice.example";
    private const string HoldNote = "Waiting for the replacement fan motor quote to be approved.";

    private static readonly UserReference _marco = new(JobDtoBuilder.Marco, "Marco Reyes");

    [Fact]
    public async Task StartJobSendsInProgressWithTheVersionOnScreenAndReRendersFromTheAnswer()
    {
        var (viewModel, harness, routes) = await OpenAsync(JobDtoBuilder.Details1047());
        routes.Puts.Enqueue(Answer(Started1047()));

        await viewModel.StartJobCommand.ExecuteAsync(null);

        var put = Assert.Single(harness.Stub.Requests, request => request.Method == HttpMethod.Put);
        Assert.Equal("/api/jobs/" + JobDtoBuilder.Job1047.ToString("D") + "/status", put.Path);
        var body = ProblemResponses.ReadBody(put);
        Assert.Equal("InProgress", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("note").ValueKind);
        Assert.Equal(9u, body.GetProperty("version").GetUInt32());
        Assert.Equal(2, harness.Stub.Requests.Count);
        Assert.Equal(JobStatus.InProgress, viewModel.Status);
        Assert.Equal("Started by Marco Reyes", viewModel.Timeline[^1].Text);
        Assert.True(viewModel.Timeline[^1].IsNewest);
        Assert.False(viewModel.ShowsStartJob);
        Assert.True(viewModel.ShowsUpdateStatus);
        Assert.True(viewModel.ShowsDock);
        Assert.False(viewModel.ShowsNavigationRow);
        Assert.True(viewModel.ShowsRowActions);
        Assert.Null(viewModel.DockHelper);
        harness.ScreenReader.Received(1).Announce("JOB-1047 started.");
        Assert.Equal(1, viewModel.UpdateStatusFocusRequest);
        Assert.False(viewModel.IsStarting);
    }

    [Fact]
    public async Task WhileStartingTheButtonIsBusyNotDisabledAndRepeatTapsSendNothing()
    {
        var (viewModel, harness, routes) = await OpenAsync(JobDtoBuilder.Details1047());
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        routes.Puts.Enqueue(() => release.Task);

        var starting = viewModel.StartJobCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsStarting);
        Assert.Equal("Starting…", viewModel.StartJobLabel);
        Assert.True(viewModel.StartJobCommand.CanExecute(null));
        Assert.True(viewModel.NavigateCommand.CanExecute(null));
        await viewModel.StartJobCommand.ExecuteAsync(null);
        release.SetResult(JobDtoBuilder.Json(Started1047()));
        await starting;

        Assert.Single(harness.Stub.Requests, request => request.Method == HttpMethod.Put);
        Assert.False(viewModel.IsStarting);
        Assert.Equal("Start job", viewModel.StartJobLabel);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task AFailedStartShowsTheErrorBannerAtTheTopWithItsReference(HttpStatusCode status)
    {
        var correlationId = Guid.NewGuid().ToString("D");
        var (viewModel, _, routes) = await OpenAsync(JobDtoBuilder.Details1047());
        routes.Puts.Enqueue(() => Task.FromResult(status == HttpStatusCode.BadRequest
            ? ProblemResponses.Problem(status, "request.validation-failed", "One or more fields are invalid.", correlationId, new Dictionary<string, string[]> { ["status"] = ["Choose InProgress or OnHold."] })
            : ProblemResponses.Problem(status, "server.error", "Failure.", correlationId)));

        await viewModel.StartJobCommand.ExecuteAsync(null);

        Assert.Equal(new PageBanner(BannerVariant.Error, "The job could not be started.", "Try again in a moment.", correlationId), viewModel.ActionBanner);
        Assert.True(viewModel.HasActionBanner);
        Assert.Equal(1, viewModel.ScrollToTopRequest);
        Assert.Equal(JobStatus.Assigned, viewModel.Status);
        Assert.True(viewModel.ShowsStartJob);
        Assert.False(viewModel.IsStatusSheetOpen);
    }

    [Fact]
    public async Task AStartRefusedForTooManyRequestsShowsTheWaitWithoutAReference()
    {
        var (viewModel, _, routes) = await OpenAsync(JobDtoBuilder.Details1047());
        routes.Puts.Enqueue(() => Task.FromResult(ProblemResponses.RateLimited(TimeSpan.FromSeconds(30))));

        await viewModel.StartJobCommand.ExecuteAsync(null);

        Assert.Equal(new PageBanner(BannerVariant.Error, "Too many requests.", "Try again in 30 seconds.", null), viewModel.ActionBanner);
    }

    [Fact]
    public async Task AStartWithNoResponseShowsTheUnreachableBannerWithoutAReference()
    {
        var (viewModel, _, routes) = await OpenAsync(JobDtoBuilder.Details1047());
        routes.Puts.Enqueue(() => Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection refused")));

        await viewModel.StartJobCommand.ExecuteAsync(null);

        Assert.Equal(new PageBanner(BannerVariant.Error, "FieldService could not be reached.", "Check your connection, then try again.", null), viewModel.ActionBanner);
    }

    [Fact]
    public async Task AStartThatCannotHappenAnyMoreReloadsTheJobAndExplainsTheTransition()
    {
        var (viewModel, harness, routes) = await OpenAsync(JobDtoBuilder.Details1047());
        routes.Puts.Enqueue(Problem(HttpStatusCode.Conflict, JobDetailsViewModel.InvalidTransitionCode));
        routes.Gets.Enqueue(Answer(Started1047()));

        await viewModel.StartJobCommand.ExecuteAsync(null);

        Assert.Equal(3, harness.Stub.Requests.Count);
        Assert.Equal(HttpMethod.Get, harness.Stub.Requests[2].Method);
        Assert.Equal(JobStatus.InProgress, viewModel.Status);
        Assert.Equal(
            new PageBanner(BannerVariant.Conflict, "JOB-1047 can no longer be started.", "Its status changed after you opened it. The latest version is shown.", null),
            viewModel.ActionBanner);
        Assert.Equal(1, viewModel.ScrollToTopRequest);
        Assert.True(viewModel.ShowsUpdateStatus);
        Assert.Equal(1, viewModel.TitleFocusRequest);
        harness.ScreenReader.DidNotReceive().Announce(Arg.Any<string>());
    }

    [Fact]
    public async Task AStaleVersionReloadsTheJobAndSaysItChangedWithFocusBackOnTheButton()
    {
        var (viewModel, _, routes) = await OpenAsync(JobDtoBuilder.Details1047());
        routes.Puts.Enqueue(Problem(HttpStatusCode.Conflict, JobDetailsViewModel.ConcurrencyConflictCode));
        routes.Gets.Enqueue(Answer(JobDtoBuilder.Details1047() with { Version = 10, Description = "Updated by dispatch." }));

        await viewModel.StartJobCommand.ExecuteAsync(null);

        Assert.Equal(
            new PageBanner(BannerVariant.Conflict, "JOB-1047 changed after you opened it.", "The latest version is shown. Check it, then try again.", null),
            viewModel.ActionBanner);
        Assert.Equal("Updated by dispatch.", viewModel.Description);
        Assert.Equal(1, viewModel.StartJobFocusRequest);
        Assert.Equal(0, viewModel.TitleFocusRequest);
    }

    [Fact]
    public async Task WhenTheReloadAfterAConflictFailsTheRefreshBannerShowsInsteadOfTheConflict()
    {
        var correlationId = Guid.NewGuid().ToString("D");
        var (viewModel, _, routes) = await OpenAsync(JobDtoBuilder.Details1047());
        routes.Puts.Enqueue(Problem(HttpStatusCode.Conflict, JobDetailsViewModel.ConcurrencyConflictCode));
        routes.Gets.Enqueue(() => Task.FromResult(ProblemResponses.ServerError(correlationId)));

        await viewModel.StartJobCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ActionBanner);
        Assert.True(viewModel.HasRefreshError);
        Assert.Equal("The job could not be refreshed.", viewModel.RefreshError?.Title);
        Assert.Equal(correlationId, viewModel.RefreshError?.Reference);
        Assert.Equal(ScreenState.Content, viewModel.State);
    }

    [Fact]
    public async Task AJobNoLongerAssignedReturnsToMyJobsWithTheNotice()
    {
        var (viewModel, harness, routes) = await OpenAsync(JobDtoBuilder.Details1042());
        routes.Puts.Enqueue(() => Task.FromResult(ProblemResponses.NotFound()));
        viewModel.OpenStatusSheetCommand.Execute(null);
        viewModel.HoldReason = HoldNote;

        await viewModel.ConfirmStatusCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsStatusSheetOpen);
        await harness.Navigator.Received(1).GoToAsync(
            Routes.Back,
            Arg.Is<IDictionary<string, object>>(query => (string)query[Routes.NoticeParameter] == Routes.JobUnassignedNotice));
    }

    [Fact]
    public async Task OfflineTheStatusActionsTakeTheDisabledLookWithTheOfflineHelper()
    {
        var (assigned, assignedHarness, _) = await OpenAsync(JobDtoBuilder.Details1047());
        var (started, startedHarness, _) = await OpenAsync(JobDtoBuilder.Details1042());

        assignedHarness.SetOnline(false);
        startedHarness.SetOnline(false);

        Assert.False(assigned.StartJobCommand.CanExecute(null));
        Assert.True(assigned.ShowsStartJob);
        Assert.Equal("You're offline. Connect to start the job.", assigned.DockHelper);
        Assert.False(started.OpenStatusSheetCommand.CanExecute(null));
        Assert.True(started.ShowsUpdateStatus);
        Assert.Equal("You're offline. Connect to update the status.", started.DockHelper);
        Assert.False(assigned.IsStarting);

        assignedHarness.SetOnline(true);

        Assert.True(assigned.StartJobCommand.CanExecute(null));
        Assert.Equal("Dispatch sees the job as In progress as soon as you start.", assigned.DockHelper);
    }

    [Fact]
    public async Task CompletedAndCancelledJobsOfferNoStatusAction()
    {
        foreach (var job in new[] { JobDtoBuilder.Details1039(), JobDtoBuilder.Details1036() })
        {
            var (viewModel, _, _) = await OpenAsync(job);

            Assert.False(viewModel.ShowsDock);
            Assert.False(viewModel.ShowsStartJob);
            Assert.False(viewModel.ShowsUpdateStatus);
            Assert.False(viewModel.OpenStatusSheetCommand.CanExecute(null));
            Assert.False(viewModel.StartJobCommand.CanExecute(null));
            Assert.Null(viewModel.DockHelper);
        }
    }

    [Fact]
    public async Task AnInProgressJobOffersPutOnHoldPreselectedWithTheReasonFieldAndADisabledCompleteJob()
    {
        var (viewModel, _, _) = await OpenAsync(JobDtoBuilder.Details1042());

        viewModel.OpenStatusSheetCommand.Execute(null);

        Assert.True(viewModel.IsStatusSheetOpen);
        Assert.True(viewModel.ShowsPhoneSheet);
        Assert.Equal([StatusAction.PutOnHold, StatusAction.Complete], viewModel.StatusOptions.Select(candidate => candidate.Action));
        Assert.False(viewModel.StatusOptions[1].IsAvailable);
        Assert.Equal(StatusOption.CompleteIncompleteHelper, viewModel.StatusOptions[1].Helper);
        var option = viewModel.StatusOptions[0];
        Assert.Equal(StatusAction.PutOnHold, option.Action);
        Assert.Equal("Put on hold", option.Title);
        Assert.Equal("Waiting on parts, access or the customer", option.Helper);
        Assert.Equal("FsIconPauseCircle", option.IconKey);
        Assert.Equal("FsStatusOnHold", option.IconColorKey);
        Assert.True(option.IsSelected);
        Assert.Same(option, viewModel.SelectedStatusOption);
        Assert.True(viewModel.ShowsHoldReason);
        Assert.Equal("Put on hold", viewModel.ConfirmText);
        Assert.Equal("Putting on hold…", viewModel.ConfirmBusyText);
        Assert.Equal("FsIconPauseCircle", viewModel.ConfirmIconKey);
        Assert.Equal("JOB-1042 · Rooftop HVAC unit not cooling", viewModel.SheetSubtitle);
    }

    [Fact]
    public async Task AnOnHoldJobOffersResumeWithoutTheReasonFieldAndACompleteJobRowThatAsksToResumeFirst()
    {
        var (viewModel, _, _) = await OpenAsync(JobDtoBuilder.Details1037());

        viewModel.OpenStatusSheetCommand.Execute(null);

        Assert.Equal([StatusAction.Resume, StatusAction.Complete], viewModel.StatusOptions.Select(candidate => candidate.Action));
        Assert.False(viewModel.StatusOptions[1].IsAvailable);
        Assert.Equal(StatusOption.CompleteResumeHelper, viewModel.StatusOptions[1].Helper);
        var option = viewModel.StatusOptions[0];
        Assert.Equal(StatusAction.Resume, option.Action);
        Assert.Equal("Resume", option.Title);
        Assert.Equal("Carry on where you left off", option.Helper);
        Assert.Equal("FsIconPlayCircle", option.IconKey);
        Assert.Equal("FsStatusInProgress", option.IconColorKey);
        Assert.True(option.IsSelected);
        Assert.False(viewModel.ShowsHoldReason);
        Assert.Equal("Resume job", viewModel.ConfirmText);
        Assert.Equal("Resuming…", viewModel.ConfirmBusyText);
    }

    [Fact]
    public async Task OnlyTheServersChangesAreOfferedAndAStatusTheAppDoesNotKnowIsLeftOut()
    {
        var job = JobDtoBuilder.Details1042() with
        {
            Affordances = new JobAffordances(
                [new AllowedStatusChange(JobStatus.Unknown, false), new AllowedStatusChange(JobStatus.Completed, false), new AllowedStatusChange(JobStatus.OnHold, true)],
                false),
        };
        var (viewModel, _, _) = await OpenAsync(job);
        var (none, _, _) = await OpenAsync(JobDtoBuilder.Details1042() with { Affordances = new JobAffordances([], false) });

        viewModel.OpenStatusSheetCommand.Execute(null);

        Assert.Equal([StatusAction.PutOnHold, StatusAction.Complete], viewModel.StatusOptions.Select(option => option.Action));
        none.OpenStatusSheetCommand.Execute(null);
        Assert.Equal([StatusAction.Complete], none.StatusOptions.Select(option => option.Action));
        Assert.True(none.ShowsUpdateStatus);
    }

    [Fact]
    public async Task TheNoteFieldFollowsRequiresNoteNotTheStatus()
    {
        var job = JobDtoBuilder.Details1037() with { Affordances = new JobAffordances([new AllowedStatusChange(JobStatus.InProgress, true)], false) };
        var (viewModel, _, _) = await OpenAsync(job);

        viewModel.OpenStatusSheetCommand.Execute(null);

        Assert.Equal(StatusAction.Resume, viewModel.SelectedStatusOption?.Action);
        Assert.True(viewModel.ShowsHoldReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnEmptyReasonShowsTheRequiredErrorFocusesTheFieldAndSendsNothing(string reason)
    {
        var (viewModel, harness, _) = await OpenAsync(JobDtoBuilder.Details1042());
        viewModel.OpenStatusSheetCommand.Execute(null);
        viewModel.HoldReason = reason;

        await viewModel.ConfirmStatusCommand.ExecuteAsync(null);

        Assert.Equal("Enter a reason for putting the job on hold.", viewModel.HoldReasonError);
        Assert.Equal(1, viewModel.HoldReasonFocusRequest);
        Assert.True(viewModel.IsStatusSheetOpen);
        Assert.True(viewModel.ConfirmStatusCommand.CanExecute(null));
        Assert.DoesNotContain(harness.Stub.Requests, request => request.Method == HttpMethod.Put);

        viewModel.HoldReason = " ";
        Assert.NotNull(viewModel.HoldReasonError);
        viewModel.HoldReason = " W";
        Assert.Null(viewModel.HoldReasonError);
    }

    [Fact]
    public async Task APutOnHoldSendsTheTrimmedReasonAndReRendersTheJobAndClearsTheDraft()
    {
        var (viewModel, harness, routes) = await OpenAsync(JobDtoBuilder.Details1042());
        routes.Puts.Enqueue(Answer(OnHold1042(HoldNote)));
        viewModel.OpenStatusSheetCommand.Execute(null);
        viewModel.HoldReason = "  " + HoldNote + "\n ";

        await viewModel.ConfirmStatusCommand.ExecuteAsync(null);

        var body = ProblemResponses.ReadBody(Assert.Single(harness.Stub.Requests, request => request.Method == HttpMethod.Put));
        Assert.Equal("OnHold", body.GetProperty("status").GetString());
        Assert.Equal(HoldNote, body.GetProperty("note").GetString());
        Assert.Equal(11u, body.GetProperty("version").GetUInt32());
        Assert.False(viewModel.IsStatusSheetOpen);
        Assert.Equal(string.Empty, viewModel.HoldReason);
        Assert.Equal(JobStatus.OnHold, viewModel.Status);
        Assert.Equal("“" + HoldNote + "”", viewModel.HoldReasonLine);
        Assert.True(viewModel.HasHoldReasonLine);
        Assert.Equal("Put on hold by Marco Reyes", viewModel.Timeline[^1].Text);
        Assert.Equal(HoldNote, viewModel.Timeline[^1].Note);
        Assert.True(viewModel.ShowsUpdateStatus);
        harness.ScreenReader.Received(1).Announce("JOB-1042 put on hold.");
        Assert.Equal(1, viewModel.UpdateStatusFocusRequest);
    }

    [Fact]
    public async Task ResumeSendsInProgressWithNoNote()
    {
        var (viewModel, harness, routes) = await OpenAsync(JobDtoBuilder.Details1037());
        routes.Puts.Enqueue(Answer(Resumed1037()));
        viewModel.OpenStatusSheetCommand.Execute(null);

        await viewModel.ConfirmStatusCommand.ExecuteAsync(null);

        var body = ProblemResponses.ReadBody(Assert.Single(harness.Stub.Requests, request => request.Method == HttpMethod.Put));
        Assert.Equal("InProgress", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("note").ValueKind);
        Assert.Equal(JobStatus.InProgress, viewModel.Status);
        Assert.Null(viewModel.HoldReasonLine);
        Assert.Equal("Resumed by Tom Becker", viewModel.Timeline[^1].Text);
        harness.ScreenReader.Received(1).Announce("JOB-1037 resumed.");
    }

    [Fact]
    public async Task ASameStatusAnswerReRendersFromTheBodyWithTheReasonSavedTheFirstTime()
    {
        var first = OnHold1042("Waiting for the roof key.");
        var (viewModel, harness, routes) = await OpenAsync(JobDtoBuilder.Details1042());
        routes.Puts.Enqueue(Answer(first));
        viewModel.OpenStatusSheetCommand.Execute(null);
        viewModel.HoldReason = HoldNote;

        await viewModel.ConfirmStatusCommand.ExecuteAsync(null);

        Assert.Equal("“Waiting for the roof key.”", viewModel.HoldReasonLine);
        Assert.Equal(first.History.Count, viewModel.Timeline.Count);
        harness.ScreenReader.Received(1).Announce("JOB-1042 put on hold.");
    }

    [Fact]
    public async Task AReasonRefusedByTheServerShowsItsMessageOnTheField()
    {
        var (viewModel, _, routes) = await OpenAsync(JobDtoBuilder.Details1042());
        routes.Puts.Enqueue(() => Task.FromResult(ProblemResponses.ValidationFailed(new Dictionary<string, string[]>
        {
            ["note"] = ["Enter a note of at most 500 characters to put the job on hold."],
        })));
        viewModel.OpenStatusSheetCommand.Execute(null);
        viewModel.HoldReason = HoldNote;

        await viewModel.ConfirmStatusCommand.ExecuteAsync(null);

        Assert.Equal("Enter a note of at most 500 characters to put the job on hold.", viewModel.HoldReasonError);
        Assert.Equal(1, viewModel.HoldReasonFocusRequest);
        Assert.True(viewModel.IsStatusSheetOpen);
        Assert.Null(viewModel.SheetFailure);
        Assert.Equal(HoldNote, viewModel.HoldReason);
    }

    [Theory]
    [InlineData("unreachable", "FieldService could not be reached.", "Check your connection, then try again.", false)]
    [InlineData("rate-limited", "Too many requests.", "Try again in 30 seconds.", false)]
    [InlineData("forbidden", "The job could not be put on hold.", "Try again in a moment.", true)]
    [InlineData("invalid", "The job could not be put on hold.", "Try again in a moment.", true)]
    public async Task ASheetFailureShowsInsideTheSheetAndKeepsTheChoiceAndTheReason(string failure, string title, string message, bool hasReference)
    {
        var (viewModel, harness, routes) = await OpenAsync(JobDtoBuilder.Details1042());
        routes.Puts.Enqueue(() => failure switch
        {
            "unreachable" => Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection refused")),
            "rate-limited" => Task.FromResult(ProblemResponses.RateLimited(TimeSpan.FromSeconds(30))),
            "forbidden" => Task.FromResult(ProblemResponses.Problem(HttpStatusCode.Forbidden, "auth.forbidden", "Forbidden.")),
            _ => Task.FromResult(ProblemResponses.ValidationFailed(new Dictionary<string, string[]> { ["status"] = ["Choose InProgress or OnHold."] })),
        });
        viewModel.OpenStatusSheetCommand.Execute(null);
        viewModel.HoldReason = HoldNote;

        await viewModel.ConfirmStatusCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsStatusSheetOpen);
        Assert.True(viewModel.HasSheetFailure);
        Assert.Equal(title, viewModel.SheetFailure?.Title);
        Assert.Equal(message, viewModel.SheetFailure?.Message);
        Assert.Equal(hasReference, viewModel.SheetFailure?.HasReference);
        Assert.Equal(1, viewModel.SheetScrollRequest);
        Assert.Null(viewModel.ActionBanner);
        Assert.Equal(HoldNote, viewModel.HoldReason);
        Assert.True(viewModel.StatusOptions.Single(option => option.Action == StatusAction.PutOnHold).IsSelected);
        Assert.Equal(JobStatus.InProgress, viewModel.Status);
        Assert.Single(harness.Stub.Requests, request => request.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task CancelTheOverlayAndBackCloseTheSheetAndKeepTheDraft()
    {
        var (viewModel, _, _) = await OpenAsync(JobDtoBuilder.Details1042());
        viewModel.OpenStatusSheetCommand.Execute(null);
        viewModel.HoldReason = HoldNote;

        viewModel.CloseStatusSheetCommand.Execute(null);
        Assert.False(viewModel.IsStatusSheetOpen);
        viewModel.OpenStatusSheetCommand.Execute(null);
        Assert.True(viewModel.CloseSheet());
        Assert.False(viewModel.CloseSheet());
        viewModel.OpenStatusSheetCommand.Execute(null);

        Assert.True(viewModel.IsStatusSheetOpen);
        Assert.Equal(HoldNote, viewModel.HoldReason);
        Assert.True(viewModel.StatusOptions.Single(option => option.Action == StatusAction.PutOnHold).IsSelected);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("back")]
    [InlineData("hold")]
    [InlineData("conflict")]
    [InlineData("gone")]
    public async Task EveryWayTheStatusSheetClosesTurnsTheSheetsOpenBindingOffSoItsFieldLetsGoOfTheKeyboard(string path)
    {
        var (viewModel, _, routes) = await OpenAsync(JobDtoBuilder.Details1042());
        switch (path)
        {
            case "hold":
                routes.Puts.Enqueue(Answer(OnHold1042(HoldNote)));
                break;
            case "conflict":
                routes.Puts.Enqueue(Problem(HttpStatusCode.Conflict, JobDetailsViewModel.ConcurrencyConflictCode));
                routes.Gets.Enqueue(Answer(JobDtoBuilder.Details1042() with { Version = 12 }));
                break;
            case "gone":
                routes.Puts.Enqueue(() => Task.FromResult(ProblemResponses.NotFound()));
                break;
            default:
                break;
        }

        viewModel.OpenStatusSheetCommand.Execute(null);
        viewModel.HoldReason = HoldNote;
        Assert.True(viewModel.ShowsPhoneSheet);
        var sheetStates = new List<bool>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(JobDetailsViewModel.ShowsPhoneSheet))
            {
                sheetStates.Add(viewModel.ShowsPhoneSheet);
            }
        };

        switch (path)
        {
            case "cancel":
                viewModel.CloseStatusSheetCommand.Execute(null);
                break;
            case "back":
                Assert.True(viewModel.CloseSheet());
                break;
            default:
                await viewModel.ConfirmStatusCommand.ExecuteAsync(null);
                break;
        }

        Assert.False(viewModel.ShowsPhoneSheet);
        Assert.False(viewModel.ShowsPaneSheet);
        Assert.NotEmpty(sheetStates);
        Assert.False(sheetStates[^1]);
    }

    [Fact]
    public async Task WhileTheSheetIsBusyCancelBackTheRowsAndTheReasonTakeNoInput()
    {
        var (viewModel, harness, routes) = await OpenAsync(JobDtoBuilder.Details1037() with
        {
            Affordances = new JobAffordances([new AllowedStatusChange(JobStatus.InProgress, false), new AllowedStatusChange(JobStatus.OnHold, true)], false),
        });
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        routes.Puts.Enqueue(() => release.Task);
        viewModel.OpenStatusSheetCommand.Execute(null);
        var resume = viewModel.StatusOptions.Single(option => option.Action == StatusAction.Resume);
        var hold = viewModel.StatusOptions.Single(option => option.Action == StatusAction.PutOnHold);
        viewModel.SelectStatusOptionCommand.Execute(resume);

        var confirming = viewModel.ConfirmStatusCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsUpdatingStatus);
        Assert.True(viewModel.IsReasonReadOnly);
        Assert.True(viewModel.ConfirmStatusCommand.CanExecute(null));
        viewModel.CloseStatusSheetCommand.Execute(null);
        Assert.True(viewModel.CloseSheet());
        viewModel.SelectStatusOptionCommand.Execute(hold);
        await viewModel.ConfirmStatusCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsStatusSheetOpen);
        Assert.Same(resume, viewModel.SelectedStatusOption);
        Assert.True(resume.IsSelected);
        Assert.False(hold.IsSelected);

        release.SetResult(JobDtoBuilder.Json(Resumed1037()));
        await confirming;

        Assert.Single(harness.Stub.Requests, request => request.Method == HttpMethod.Put);
        Assert.False(viewModel.IsStatusSheetOpen);
        Assert.False(viewModel.IsReasonReadOnly);
    }

    [Theory]
    [InlineData(JobDetailsViewModel.ConcurrencyConflictCode, "JOB-1042 changed after you opened it.", "The latest version is shown. Check it, then try again.")]
    [InlineData(JobDetailsViewModel.InvalidTransitionCode, "JOB-1042 can no longer be put on hold.", "Its status changed after you opened it. The latest version is shown.")]
    public async Task AConflictWhilePuttingOnHoldClosesTheSheetReloadsAndExplainsAndKeepsTheReason(string code, string title, string message)
    {
        var (viewModel, harness, routes) = await OpenAsync(JobDtoBuilder.Details1042());
        routes.Puts.Enqueue(Problem(HttpStatusCode.Conflict, code));
        routes.Gets.Enqueue(Answer(JobDtoBuilder.Details1042() with { Version = 12 }));
        viewModel.OpenStatusSheetCommand.Execute(null);
        viewModel.HoldReason = HoldNote;

        await viewModel.ConfirmStatusCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsStatusSheetOpen);
        Assert.Equal(new PageBanner(BannerVariant.Conflict, title, message, null), viewModel.ActionBanner);
        Assert.Equal(1, viewModel.ScrollToTopRequest);
        Assert.Equal(1, viewModel.UpdateStatusFocusRequest);
        Assert.Equal(12u, viewModel.Job?.Version);
        Assert.Equal(HoldNote, viewModel.HoldReason);
        Assert.Equal(HttpMethod.Get, harness.Stub.Requests[^1].Method);

        viewModel.OpenStatusSheetCommand.Execute(null);
        Assert.Equal(HoldNote, viewModel.HoldReason);
    }

    [Fact]
    public async Task AnOpenSheetStaysOpenWhenTheDeviceGoesOfflineAndItsConfirmFailsAsNoResponse()
    {
        var (viewModel, harness, _) = await OpenAsync(JobDtoBuilder.Details1042());
        viewModel.OpenStatusSheetCommand.Execute(null);
        viewModel.HoldReason = HoldNote;

        harness.SetOnline(false);
        await viewModel.ConfirmStatusCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsStatusSheetOpen);
        Assert.Equal("FieldService could not be reached.", viewModel.SheetFailure?.Title);
        Assert.False(viewModel.SheetFailure?.HasReference);
        Assert.DoesNotContain(harness.Stub.Requests, request => request.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task TheOnHoldHeroShowsTheLatestReasonAndLeavesItOutWhenThereIsNone()
    {
        var (held, _, _) = await OpenAsync(JobDtoBuilder.Details1037());
        var (blank, _, _) = await OpenAsync(JobDtoBuilder.Details1037() with
        {
            History = [JobDtoBuilder.History(JobHistoryAction.PutOnHold, JobStatus.InProgress, JobStatus.OnHold, _marco, null, "  ", JobDtoBuilder.Local(10, 16, 13, 50))],
        });

        Assert.Equal("“Waiting for the gas utility to restore supply to the boiler room.”", held.HoldReasonLine);
        Assert.True(held.HasHoldReasonLine);
        Assert.Null(blank.HoldReasonLine);
        Assert.False(blank.HasHoldReasonLine);
    }

    [Fact]
    public async Task AChangeAnsweredAfterTheSessionEndedIsDroppedWithoutABanner()
    {
        var (viewModel, harness, routes) = await OpenAsync(JobDtoBuilder.Details1042());
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        routes.Puts.Enqueue(() => release.Task);
        viewModel.OpenStatusSheetCommand.Execute(null);
        viewModel.HoldReason = HoldNote;
        var confirming = viewModel.ConfirmStatusCommand.ExecuteAsync(null);

        harness.EndSession();
        release.SetResult(ProblemResponses.ServerError(Guid.NewGuid().ToString("D")));
        await confirming;

        Assert.False(viewModel.IsStatusSheetOpen);
        Assert.Equal(string.Empty, viewModel.HoldReason);
        Assert.Null(viewModel.SheetFailure);
        Assert.Null(viewModel.ActionBanner);
        Assert.Null(viewModel.Job);
        harness.ScreenReader.DidNotReceive().Announce(Arg.Any<string>());
    }

    [Fact]
    public async Task AStartAnsweredUnauthorizedAfterARevokedSessionEndsItThroughTheSessionServiceAndShowsNothing()
    {
        var router = new SessionRouter();
        var session = TestSession.Create(new StubHttpMessageHandler(router.RespondAsync));
        await session.Session.SignInAsync(MarcoEmail, TestSession.NewPassword(), CancellationToken.None);
        var harness = new JobsHarness(session.Stub, session: session.Session);
        var viewModel = harness.CreateDetails();
        viewModel.AppearingCommand.Execute(null);
        await viewModel.ShowJobAsync(JobDtoBuilder.Job1047);
        router.Revoke();

        await viewModel.StartJobCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ActionBanner);
        Assert.Null(viewModel.Job);
        Assert.Equal(SessionEndReason.Expired, session.Session.EndReason);
        await session.Navigator.Received(1).GoToAsync(Routes.SignIn);
        Assert.Single(session.Stub.Requests, request => request.Method == HttpMethod.Put);
        Assert.DoesNotContain(session.Stub.Requests, request => request.Path == "/api/auth/sign-out");
        harness.ScreenReader.DidNotReceive().Announce(Arg.Any<string>());
    }

    [Fact]
    public async Task TheDraftAndTheSheetAreForgottenWhenAnotherJobOpens()
    {
        var (viewModel, _, routes) = await OpenAsync(JobDtoBuilder.Details1042());
        routes.Gets.Enqueue(Answer(JobDtoBuilder.Details1037()));
        viewModel.OpenStatusSheetCommand.Execute(null);
        viewModel.HoldReason = HoldNote;

        await viewModel.ShowJobAsync(JobDtoBuilder.Job1037);

        Assert.False(viewModel.IsStatusSheetOpen);
        Assert.Equal(string.Empty, viewModel.HoldReason);
        Assert.Empty(viewModel.StatusOptions);
    }

    [Fact]
    public async Task OnTheTwoPaneLayoutAChangeReloadsTheListAndBackClosesThePanesSheetFirst()
    {
        var routes = new StatusRoutes();
        routes.Gets.Enqueue(Answer(JobDtoBuilder.MarcoActive()));
        routes.Gets.Enqueue(Answer(JobDtoBuilder.Details1042()));
        routes.Puts.Enqueue(Answer(OnHold1042(HoldNote)));
        routes.Gets.Enqueue(Answer(JobDtoBuilder.MarcoActive()));
        var harness = new JobsHarness(new StubHttpMessageHandler(routes.RespondAsync), twoPane: true);
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        var job = viewModel.Groups[0][0];
        viewModel.SelectedJob = job;
        await viewModel.OpenJobCommand.ExecuteAsync(job);

        viewModel.Details.OpenStatusSheetCommand.Execute(null);
        Assert.True(viewModel.HasOpenSheet);
        Assert.True(viewModel.Details.ShowsPaneSheet);
        Assert.False(viewModel.Details.ShowsPhoneSheet);
        Assert.True(viewModel.CloseSheet());
        Assert.False(viewModel.HasOpenSheet);
        viewModel.Details.OpenStatusSheetCommand.Execute(null);
        viewModel.Details.HoldReason = HoldNote;
        await viewModel.Details.ConfirmStatusCommand.ExecuteAsync(null);

        await WaitFor(() => harness.Stub.Requests.Count == 4 && !viewModel.IsBusy);
        Assert.Equal("/api/technicians/me/jobs", harness.Stub.Requests[3].Path);
        Assert.Equal(JobStatus.OnHold, viewModel.Details.Status);
    }

    [Fact]
    public async Task StatusLogsHoldIdentifiersAndNeverTheReason()
    {
        var (viewModel, harness, routes) = await OpenAsync(JobDtoBuilder.Details1042());
        routes.Puts.Enqueue(() => Task.FromResult(ProblemResponses.ServerError(Guid.NewGuid().ToString("D"))));
        routes.Puts.Enqueue(Answer(OnHold1042(HoldNote)));
        viewModel.OpenStatusSheetCommand.Execute(null);
        viewModel.HoldReason = HoldNote;

        await viewModel.ConfirmStatusCommand.ExecuteAsync(null);
        await viewModel.ConfirmStatusCommand.ExecuteAsync(null);

        var text = harness.Logs.AllText();
        Assert.Contains("jobs.status-change-failed " + JobDtoBuilder.Job1042 + " OnHold 500", text, StringComparison.Ordinal);
        Assert.Contains("jobs.status-changed " + JobDtoBuilder.Job1042 + " OnHold", text, StringComparison.Ordinal);
        Assert.DoesNotContain("replacement fan motor", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Brightwater", text, StringComparison.Ordinal);
    }

    private static async Task<(JobDetailsViewModel ViewModel, JobsHarness Harness, StatusRoutes Routes)> OpenAsync(JobDetailsResponse job)
    {
        var routes = new StatusRoutes();
        routes.Gets.Enqueue(Answer(job));
        var harness = new JobsHarness(new StubHttpMessageHandler(routes.RespondAsync));
        var viewModel = harness.CreateDetails();
        viewModel.AppearingCommand.Execute(null);
        await viewModel.ShowJobAsync(job.Id);
        Assert.Equal(ScreenState.Content, viewModel.State);
        return (viewModel, harness, routes);
    }

    private static Func<Task<HttpResponseMessage>> Answer(JobDetailsResponse job) => () => Task.FromResult(JobDtoBuilder.Json(job));

    private static Func<Task<HttpResponseMessage>> Answer(GetAssignedJobsResponse jobs) => () => Task.FromResult(JobDtoBuilder.Json(jobs));

    private static Func<Task<HttpResponseMessage>> Problem(HttpStatusCode status, string code) =>
        () => Task.FromResult(ProblemResponses.Problem(status, code, "The job cannot move to that status."));

    private static JobDetailsResponse Started1047()
    {
        var job = JobDtoBuilder.Details1047();
        return job with
        {
            Status = JobStatus.InProgress,
            StartedAt = JobDtoBuilder.Local(10, 19, 10, 44),
            History = [.. job.History, JobDtoBuilder.History(JobHistoryAction.Started, JobStatus.Assigned, JobStatus.InProgress, _marco, null, null, JobDtoBuilder.Local(10, 19, 10, 44))],
            Version = 10,
            Affordances = new JobAffordances([new AllowedStatusChange(JobStatus.OnHold, true)], false),
        };
    }

    private static JobDetailsResponse OnHold1042(string note)
    {
        var job = JobDtoBuilder.Details1042();
        return job with
        {
            Status = JobStatus.OnHold,
            History = [.. job.History, JobDtoBuilder.History(JobHistoryAction.PutOnHold, JobStatus.InProgress, JobStatus.OnHold, _marco, null, note, JobDtoBuilder.Local(10, 19, 10, 31))],
            Version = 12,
            Affordances = new JobAffordances([new AllowedStatusChange(JobStatus.InProgress, false)], false),
        };
    }

    private static JobDetailsResponse Resumed1037()
    {
        var job = JobDtoBuilder.Details1037();
        var tom = new UserReference(JobDtoBuilder.Tom, "Tom Becker");
        return job with
        {
            Status = JobStatus.InProgress,
            History = [.. job.History, JobDtoBuilder.History(JobHistoryAction.Resumed, JobStatus.OnHold, JobStatus.InProgress, tom, null, null, JobDtoBuilder.Local(10, 19, 10, 40))],
            Version = 13,
            Affordances = new JobAffordances([new AllowedStatusChange(JobStatus.OnHold, true)], false),
        };
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(5);
        }

        Assert.True(condition());
    }

    private sealed class StatusRoutes
    {
        public ConcurrentQueue<Func<Task<HttpResponseMessage>>> Gets { get; } = new();

        public ConcurrentQueue<Func<Task<HttpResponseMessage>>> Puts { get; } = new();

        public Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var queue = request.Method == HttpMethod.Put ? Puts : Gets;
            return queue.TryDequeue(out var next)
                ? next()
                : throw new InvalidOperationException("No " + request.Method + " response is left.");
        }
    }

    private sealed class SessionRouter
    {
        private readonly ConcurrentDictionary<string, bool> _validTokens = new(StringComparer.Ordinal);
        private bool _revoked;

        public void Revoke()
        {
            _validTokens.Clear();
            _revoked = true;
        }

        public Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/api/auth/sign-in":
                    var accessToken = TestSession.NewAccessToken();
                    _validTokens[accessToken] = true;
                    return Task.FromResult(ProblemResponses.Session(accessToken, TestSession.NewRefreshToken(), JobDtoBuilder.Marco, "Marco Reyes"));
                case "/api/auth/refresh":
                    return Task.FromResult(_revoked ? ProblemResponses.SessionExpired() : ProblemResponses.ServerError(Guid.NewGuid().ToString("D")));
                default:
                    var bearer = request.Headers.Authorization?.Parameter;
                    return Task.FromResult(bearer is not null && _validTokens.ContainsKey(bearer)
                        ? JobDtoBuilder.Json(JobDtoBuilder.Details1047())
                        : ProblemResponses.Unauthenticated());
            }
        }
    }
}
