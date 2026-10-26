using FieldService.Mobile.Features.Inspection;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Shared.Navigation;
using FieldService.Mobile.Tests.Shared.Testing;
using NSubstitute;

namespace FieldService.Mobile.Tests.Features.Jobs;

public sealed class CompleteJobTests
{
    private static readonly SubmitAffordance _allowed = new(true, null, 10, 10);

    [Fact]
    public async Task AnInProgressJobShowsTheChecklistLinkCardAndTheContinueInspectionDock()
    {
        var (viewModel, harness) = await OpenAsync(JobDtoBuilder.Details1042());

        Assert.True(viewModel.ShowsChecklistLink);
        Assert.False(viewModel.ShowsChecklistRow);
        Assert.Equal("6 of 10 answered", viewModel.ChecklistSubline);
        Assert.Equal("Rooftop HVAC service, 6 of 10 answered, opens the inspection", viewModel.ChecklistLinkDescription);
        Assert.True(viewModel.ShowsInspectionAction);
        Assert.Equal("Continue inspection", viewModel.InspectionActionText);
        Assert.True(viewModel.ShowsUpdateStatusSecondary);
        Assert.False(viewModel.ShowsUpdateStatusPrimary);
        Assert.True(viewModel.ShowsDock);
        Assert.Equal(new InspectionJob(JobDtoBuilder.Job1042, 1042, "Rooftop HVAC unit not cooling", "Rooftop HVAC service"), harness.Store.JobFor(JobDtoBuilder.Job1042));
    }

    [Fact]
    public async Task AChecklistWithNoSavedAnswerOffersStartInspection()
    {
        var job = JobDtoBuilder.Details1042();
        var (viewModel, _) = await OpenAsync(job with { Affordances = job.Affordances with { CanSubmit = new SubmitAffordance(false, SubmitAffordance.IncompleteCode, 0, 10) } });

        Assert.Equal("Start inspection", viewModel.InspectionActionText);
        Assert.Equal("0 of 10 answered", viewModel.ChecklistSubline);
    }

    [Fact]
    public async Task UnsavedAnswersOnTheDeviceJoinTheLinkCardSubline()
    {
        var (viewModel, harness) = await OpenAsync(JobDtoBuilder.Details1042());

        foreach (var position in new[] { 7, 8, 9, 10 })
        {
            harness.Store.Put(JobDtoBuilder.Job1042, InspectionDtoBuilder.ItemId(position), new DraftAnswer(InspectionResult.Pass, string.Empty, new Dictionary<Guid, string>()));
        }

        Assert.Equal(Spaces.Text("6 of 10 answered_· 4 not saved"), viewModel.ChecklistSubline);
    }

    [Fact]
    public async Task AnOnHoldJobKeepsTheChecklistLinkAndOnlyThePrimaryUpdateStatus()
    {
        var (viewModel, _) = await OpenAsync(JobDtoBuilder.Details1037());

        Assert.True(viewModel.ShowsChecklistLink);
        Assert.False(viewModel.ShowsInspectionAction);
        Assert.True(viewModel.ShowsUpdateStatusPrimary);
        Assert.False(viewModel.ShowsUpdateStatusSecondary);
    }

    [Theory]
    [InlineData(19, "Submitted at 8:52 AM")]
    [InlineData(16, "Submitted on Fri 16 Oct at 8:52 AM")]
    public async Task ACompletedJobsLinkCardSaysWhenItWasSubmittedAndItHasNoDock(int day, string subline)
    {
        var (viewModel, _) = await OpenAsync(JobDtoBuilder.Details1039() with { CompletedAt = JobDtoBuilder.Local(10, day, 8, 52) });

        Assert.Equal(subline, viewModel.ChecklistSubline);
        Assert.False(viewModel.ShowsDock);
    }

    [Fact]
    public async Task ACancelledJobsLinkCardSaysTheChecklistWasNotSubmitted()
    {
        var job = JobDtoBuilder.Details1036();
        var (viewModel, _) = await OpenAsync(job with { Affordances = job.Affordances with { CanSubmit = new SubmitAffordance(false, SubmitAffordance.InvalidTransitionCode, 6, 10) } });

        Assert.Equal(Spaces.Text("6 of 10 answered_· not submitted"), viewModel.ChecklistSubline);
        Assert.False(viewModel.ShowsDock);
    }

    [Fact]
    public async Task AnAssignedJobKeepsTheChecklistRowThatCannotBeOpened()
    {
        var (viewModel, harness) = await OpenAsync(JobDtoBuilder.Details1047());

        Assert.True(viewModel.ShowsChecklistRow);
        Assert.False(viewModel.ShowsChecklistLink);
        await viewModel.OpenChecklistCommand.ExecuteAsync(null);
        await harness.Navigator.DidNotReceiveWithAnyArgs().GoToAsync(default!, default!);
    }

    [Fact]
    public async Task TheLinkCardOpensTheChecklistForTheJob()
    {
        var (viewModel, harness) = await OpenAsync(JobDtoBuilder.Details1042());

        await viewModel.OpenChecklistCommand.ExecuteAsync(null);

        await harness.Navigator.Received(1).GoToAsync(Routes.InspectionPage, Arg.Is<IDictionary<string, object>>(parameters =>
            (Guid)parameters[Routes.JobIdParameter] == JobDtoBuilder.Job1042));
    }

    [Fact]
    public async Task WhenTheChecklistIsCompleteTwoEnabledRowsSelectNoneAndTheConfirmButtonWaits()
    {
        var job = JobDtoBuilder.Details1042();
        var (viewModel, _) = await OpenAsync(job with { Affordances = job.Affordances with { CanSubmit = _allowed } });

        viewModel.OpenStatusSheetCommand.Execute(null);

        Assert.All(viewModel.StatusOptions, option => Assert.True(option.IsAvailable));
        Assert.All(viewModel.StatusOptions, option => Assert.False(option.IsSelected));
        Assert.Null(viewModel.SelectedStatusOption);
        Assert.Equal("Update status", viewModel.ConfirmText);
        Assert.False(viewModel.ShowsHoldReason);
        Assert.False(viewModel.ConfirmStatusCommand.CanExecute(null));
        var complete = viewModel.StatusOptions[1];
        Assert.Equal("Review the checklist and submit the job", complete.Helper);
        Assert.Equal("FsIconCheckCircle", complete.IconKey);
        Assert.Equal("FsStatusCompleted", complete.IconColorKey);
    }

    [Fact]
    public async Task ReopenedAfterAChoiceAndCancelTheTwoRowSheetsDisabledConfirmAsksForNoIcon()
    {
        var job = JobDtoBuilder.Details1042();
        var (viewModel, _) = await OpenAsync(job with { Affordances = job.Affordances with { CanSubmit = _allowed } });
        var iconKeys = new List<string?>();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(JobDetailsViewModel.ConfirmIconKey))
            {
                iconKeys.Add(viewModel.ConfirmIconKey);
            }
        };

        viewModel.OpenStatusSheetCommand.Execute(null);
        viewModel.SelectStatusOptionCommand.Execute(viewModel.StatusOptions[0]);
        Assert.Equal("FsIconPauseCircle", viewModel.ConfirmIconKey);
        viewModel.CloseStatusSheetCommand.Execute(null);
        viewModel.OpenStatusSheetCommand.Execute(null);

        Assert.Null(viewModel.SelectedStatusOption);
        Assert.Equal("Update status", viewModel.ConfirmText);
        Assert.False(viewModel.ConfirmStatusCommand.CanExecute(null));
        Assert.Null(viewModel.ConfirmIconKey);
        Assert.Equal("FsIconPauseCircle", iconKeys[0]);
        Assert.Null(iconKeys[^1]);
    }

    [Fact]
    public async Task CompleteJobTurnsTheConfirmButtonIntoReviewAndSubmitWhichOpensTheReviewWithoutARequest()
    {
        var job = JobDtoBuilder.Details1042();
        var (viewModel, harness) = await OpenAsync(job with { Affordances = job.Affordances with { CanSubmit = _allowed } });
        viewModel.OpenStatusSheetCommand.Execute(null);

        viewModel.SelectStatusOptionCommand.Execute(viewModel.StatusOptions[1]);

        Assert.Equal("Review and submit", viewModel.ConfirmText);
        Assert.Equal("FsIconClipboardCheck", viewModel.ConfirmIconKey);
        Assert.True(viewModel.ConfirmStatusCommand.CanExecute(null));
        await viewModel.ConfirmStatusCommand.ExecuteAsync(null);
        Assert.False(viewModel.IsStatusSheetOpen);
        Assert.Single(harness.Stub.Requests);
        await harness.Navigator.Received(1).GoToAsync(Routes.ReviewPage, Arg.Is<IDictionary<string, object>>(parameters =>
            (Guid)parameters[Routes.JobIdParameter] == JobDtoBuilder.Job1042 && (string)parameters[Routes.SourceParameter] == Routes.FromSheet));
    }

    [Theory]
    [InlineData(true, null, true, false, "Some checklist answers are not saved yet")]
    [InlineData(false, SubmitAffordance.IncompleteCode, false, false, "Finish the inspection checklist first")]
    [InlineData(false, SubmitAffordance.InvalidTransitionCode, false, false, "Resume the job first")]
    [InlineData(false, "auth.forbidden", false, false, "Not available right now")]
    [InlineData(true, null, false, true, "Review the checklist and submit the job")]
    public void TheCompleteJobRowFollowsCanSubmitAndTheUnsavedAnswers(bool allowed, string? code, bool unsaved, bool available, string helper)
    {
        var option = StatusOption.Complete(new SubmitAffordance(allowed, code, 10, 10), unsaved);

        Assert.Equal(available, option.IsAvailable);
        Assert.Equal(helper, option.Helper);
        Assert.Equal("Complete job", option.Title);
        Assert.Equal(StatusAction.Complete, option.Action);
    }

    [Fact]
    public async Task UnsavedAnswersDisableCompleteJobSoPutOnHoldIsSelected()
    {
        var job = JobDtoBuilder.Details1042();
        var (viewModel, harness) = await OpenAsync(job with { Affordances = job.Affordances with { CanSubmit = _allowed } });
        harness.Store.Put(JobDtoBuilder.Job1042, InspectionDtoBuilder.ItemId(7), new DraftAnswer(InspectionResult.Fail, string.Empty, new Dictionary<Guid, string>()));

        viewModel.OpenStatusSheetCommand.Execute(null);

        Assert.False(viewModel.StatusOptions[1].IsAvailable);
        Assert.Equal(StatusAction.PutOnHold, viewModel.SelectedStatusOption?.Action);
        viewModel.SelectStatusOptionCommand.Execute(viewModel.StatusOptions[1]);
        Assert.Equal(StatusAction.PutOnHold, viewModel.SelectedStatusOption?.Action);
    }

    [Fact]
    public async Task JobDetailsReloadsInTheBackgroundWhenItAppearsAgain()
    {
        var updated = JobDtoBuilder.Details1042();
        updated = updated with { Affordances = updated.Affordances with { CanSubmit = new SubmitAffordance(false, SubmitAffordance.IncompleteCode, 8, 10) } };
        var (viewModel, harness) = await OpenAsync(JobDtoBuilder.Details1042(), () => JobDtoBuilder.Json(updated));
        viewModel.DisappearingCommand.Execute(null);

        viewModel.AppearingCommand.Execute(null);
        await WaitForAsync(() => viewModel.ChecklistSubline.StartsWith("8", StringComparison.Ordinal));

        Assert.Equal(2, harness.Stub.Requests.Count);
        Assert.Equal("8 of 10 answered", viewModel.ChecklistSubline);
        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public async Task ASubmissionRefusedForItsStatusReturnsToJobDetailsWhichReloadsAndExplains()
    {
        var (viewModel, harness) = await OpenAsync(JobDtoBuilder.Details1042(), () => JobDtoBuilder.Json(JobDtoBuilder.Details1037() with { Id = JobDtoBuilder.Job1042, Number = 1042 }));

        viewModel.ApplyQueryAttributes(new Dictionary<string, object>
        {
            [Routes.JobIdParameter] = JobDtoBuilder.Job1042,
            [Routes.NoticeParameter] = Routes.SubmissionInvalidTransitionNotice,
        });
        await WaitForAsync(() => viewModel.ActionBanner is not null);

        Assert.Equal(BannerVariant.Conflict, viewModel.ActionBanner?.Variant);
        Assert.Equal("JOB-1042 can no longer be submitted.", viewModel.ActionBanner?.Title);
        Assert.Equal("Its status changed after you opened it. The latest version is shown.", viewModel.ActionBanner?.Message);
        Assert.Equal(JobStatus.OnHold, viewModel.Status);
        Assert.Equal(2, harness.Stub.Requests.Count);
    }

    [Fact]
    public async Task MyJobsShowsTheSubmittedJobsSuccessBannerForEightSecondsOfBeingShown()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())));
        var viewModel = harness.CreateMyJobs();

        viewModel.ApplyQueryAttributes(new Dictionary<string, object>
        {
            [Routes.NoticeParameter] = Routes.JobSubmittedNotice,
            [Routes.JobNumberParameter] = "JOB-1042",
        });
        await viewModel.AppearingCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasSuccess);
        Assert.Equal("JOB-1042 submitted.", viewModel.SuccessTitle);
        Assert.Equal("It is now under Completed.", MyJobsViewModel.SubmittedMessage);
        Assert.True(viewModel.IsActiveSelected);
        harness.ScreenReader.Received(1).Announce("JOB-1042 submitted.");
        harness.Clock.Advance(TimeSpan.FromSeconds(5));
        viewModel.DisappearingCommand.Execute(null);
        harness.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.True(viewModel.HasSuccess);
        await viewModel.AppearingCommand.ExecuteAsync(null);
        harness.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(viewModel.HasSuccess);
        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(viewModel.HasSuccess);
    }

    [Fact]
    public async Task BackOfflineFromTheChecklistTheInProgressDockDisablesUpdateStatusWithItsHelperAndKeepsTheInspection()
    {
        var (viewModel, harness) = await OpenAsync(JobDtoBuilder.Details1042());
        viewModel.DisappearingCommand.Execute(null);
        harness.SetOnline(false);
        var changed = new List<string?>();
        var canExecuteChanged = 0;
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        viewModel.OpenStatusSheetCommand.CanExecuteChanged += (_, _) => canExecuteChanged++;

        viewModel.AppearingCommand.Execute(null);

        Assert.Equal("You're offline. Connect to update the status.", viewModel.DockHelper);
        Assert.False(viewModel.CanUpdateStatus);
        Assert.False(viewModel.OpenStatusSheetCommand.CanExecute(null));
        Assert.True(viewModel.ShowsUpdateStatusSecondary);
        Assert.True(viewModel.ShowsInspectionAction);
        Assert.True(viewModel.OpenChecklistCommand.CanExecute(null));
        Assert.Contains(nameof(JobDetailsViewModel.DockHelper), changed);
        Assert.Contains(nameof(JobDetailsViewModel.CanUpdateStatus), changed);
        Assert.True(canExecuteChanged > 0);
    }

    [Fact]
    public async Task GoingOfflineOnTheInProgressJobDisablesUpdateStatusAndComingBackEnablesIt()
    {
        var (viewModel, harness) = await OpenAsync(JobDtoBuilder.Details1042(), () => JobDtoBuilder.Json(JobDtoBuilder.Details1042()));

        harness.SetOnline(false);
        var offline = (viewModel.CanUpdateStatus, viewModel.DockHelper, viewModel.OpenChecklistCommand.CanExecute(null));
        harness.SetOnline(true);

        Assert.Equal((false, "You're offline. Connect to update the status.", true), offline);
        Assert.True(viewModel.CanUpdateStatus);
        Assert.Null(viewModel.DockHelper);
    }

    [Fact]
    public async Task AfterTheSubmissionTheJobAndItsChecklistOpenReadOnly()
    {
        var submittedAt = JobDtoBuilder.Local(10, 19, 10, 41);
        var submitted = JobDtoBuilder.Details1042() with { Status = JobStatus.Completed, CompletedAt = submittedAt, Affordances = new JobAffordances([], false) };
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => JobDtoBuilder.Json(submitted),
            () => JobDtoBuilder.Json(submitted),
            () => InspectionDtoBuilder.Json(InspectionDtoBuilder.Complete1042(canEdit: false, status: JobStatus.Completed, submittedAt: submittedAt))));
        harness.Store.Remember(InspectionDtoBuilder.Complete1042(), JobDtoBuilder.Now);
        harness.Store.RememberJob(new InspectionJob(JobDtoBuilder.Job1042, 1042, "Rooftop HVAC unit not cooling", "Rooftop HVAC service"));
        var review = harness.CreateReview();
        review.ApplyQueryAttributes(new Dictionary<string, object> { [Routes.JobIdParameter] = JobDtoBuilder.Job1042, [Routes.SourceParameter] = Routes.FromInspection });
        review.AppearingCommand.Execute(null);
        review.IsConfirmed = true;

        await review.SubmitCommand.ExecuteAsync(null);
        var details = harness.CreateDetails();
        details.AppearingCommand.Execute(null);
        await details.ShowJobAsync(JobDtoBuilder.Job1042);
        var inspection = harness.CreateInspection();
        inspection.ApplyQueryAttributes(JobsHarness.JobQuery(JobDtoBuilder.Job1042));
        inspection.AppearingCommand.Execute(null);
        await WaitForAsync(() => inspection.Items.Count == 10 && !inspection.IsEditable && inspection.SlotBanner is not null);

        await harness.Navigator.Received(1).GoToAsync(Routes.Jobs, Arg.Is<IDictionary<string, object>>(parameters => (string)parameters[Routes.NoticeParameter] == Routes.JobSubmittedNotice));
        Assert.False(details.ShowsDock);
        Assert.False(details.ShowsInspectionAction);
        Assert.Empty(details.StatusOptions);
        Assert.Equal("Submitted at 10:41\u00A0AM", details.ChecklistSubline);
        Assert.False(inspection.IsEditable);
        Assert.False(inspection.ShowsDock);
        Assert.All(inspection.Items, item => Assert.True(item.IsReadOnly));
        Assert.Equal(BannerVariant.Locked, inspection.SlotBanner?.Variant);
        Assert.Equal("Submitted at 10:41\u00A0AM.", inspection.SlotBanner?.Title);
    }

    private static async Task<(JobDetailsViewModel ViewModel, JobsHarness Harness)> OpenAsync(JobDetailsResponse job, params Func<HttpResponseMessage>[] more)
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence([() => JobDtoBuilder.Json(job), .. more]));
        var viewModel = harness.CreateDetails();
        viewModel.AppearingCommand.Execute(null);
        await viewModel.ShowJobAsync(job.Id);
        return (viewModel, harness);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
    }
}
