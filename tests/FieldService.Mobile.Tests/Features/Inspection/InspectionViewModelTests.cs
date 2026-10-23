using System.Net;
using System.Text.Json;
using FieldService.Mobile.Features.Inspection;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Shared.Navigation;
using FieldService.Mobile.Shared.Screens;
using FieldService.Mobile.Tests.Shared.Testing;
using NSubstitute;

namespace FieldService.Mobile.Tests.Features.Inspection;

public sealed class InspectionViewModelTests
{
    [Fact]
    public async Task TheChecklistOpensWithTheSavedAnswersTheCountsAndTheItemsLeft()
    {
        var (viewModel, _) = await OpenAsync(() => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()));

        Assert.Equal(ScreenState.Content, viewModel.State);
        Assert.Equal("Rooftop HVAC service", viewModel.TemplateName);
        Assert.Equal("JOB-1042", viewModel.JobNumber);
        Assert.Equal(("6", "of 10 answered"), (viewModel.AnsweredText, viewModel.OfAnsweredText));
        Assert.Equal(("4 Pass", "1 Fail", "1 N/A"), (viewModel.PassText, viewModel.FailText, viewModel.NotApplicableText));
        Assert.Equal("4 items left to answer", viewModel.HelperText);
        Assert.True(viewModel.ShowsDock);
        Assert.True(viewModel.IsEditable);
        Assert.Equal(["118", "285"], viewModel.Items[2].Readings.Select(reading => reading.Text));
        Assert.True(viewModel.Items[4].IsFail);
        Assert.True(viewModel.Items[4].ShowsFaultField);
        Assert.Equal(InspectionDtoBuilder.FanNote, viewModel.Items[4].Note);
        Assert.True(viewModel.Items[5].ShowsNoteBox);
        Assert.Equal("03 / 10", viewModel.Items[2].NumberText);
        Assert.Equal("Item 3 of 10, Check refrigerant pressures", viewModel.Items[2].GroupDescription);
        Assert.Equal("Add note to item 01", viewModel.Items[0].NoteButtonDescription);
        Assert.Equal("Edit note for item 06", viewModel.Items[5].NoteButtonDescription);
        Assert.Equal("Supply air temperature in °F", viewModel.Items[7].Readings[0].Description);
    }

    [Fact]
    public async Task TheHelperCountsTheResultsOnScreenSavedOrNot()
    {
        var (viewModel, _) = await OpenAsync(() => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()));

        foreach (var item in viewModel.Items.Skip(6).Take(3))
        {
            item.SelectCommand.Execute(InspectionResult.NotApplicable);
        }

        Assert.Equal("1 item left to answer", viewModel.HelperText);
        viewModel.Items[9].SelectCommand.Execute(InspectionResult.Pass);
        Assert.Equal("All 10 items answered", viewModel.HelperText);
        Assert.Equal("10", viewModel.AnsweredText);
        Assert.Equal("4 N/A", viewModel.NotApplicableText);
    }

    [Fact]
    public async Task ASelectedOptionDoesNothingWhenTappedAgainAndNoControlClearsAnAnswer()
    {
        var (viewModel, harness) = await OpenAsync(() => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()));
        var item = viewModel.Items[0];

        item.SelectCommand.Execute(InspectionResult.Pass);
        item.SelectCommand.Execute(InspectionResult.Unknown);

        Assert.Equal(InspectionResult.Pass, item.Result);
        Assert.Equal(0, harness.Store.UnsavedCount(JobDtoBuilder.Job1042));
        harness.ScreenReader.DidNotReceiveWithAnyArgs().Announce(default!);
    }

    [Fact]
    public async Task NotApplicableHidesTheReadingsKeepsTheirValuesAndSendsNone()
    {
        var (viewModel, _) = await OpenAsync(() => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()));
        var item = viewModel.Items[2];

        item.SelectCommand.Execute(InspectionResult.NotApplicable);

        Assert.False(item.ShowsReadings);
        Assert.Null(item.ToInput().Readings);
        Assert.Equal(["118", "285"], item.Readings.Select(reading => reading.Text));
        item.SelectCommand.Execute(InspectionResult.Pass);
        Assert.True(item.ShowsReadings);
        Assert.Equal([118m, 285m], item.ToInput().Readings!.Select(reading => reading.Value));
    }

    [Fact]
    public async Task PassWithAnEmptyReadingSelectsPassAndMovesFocusToItWithoutAnError()
    {
        var (viewModel, _) = await OpenAsync(() => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()));
        var item = viewModel.Items[7];

        item.SelectCommand.Execute(InspectionResult.Pass);

        Assert.True(item.IsPass);
        Assert.Equal(1, item.Readings[0].FocusRequest);
        Assert.Null(item.Readings[0].Error);
        Assert.True(item.IsIncompleteAnswer);
    }

    [Fact]
    public async Task FailOpensTheRequiredFaultFieldAndMovesFocusIntoItWithoutAnError()
    {
        var (viewModel, harness) = await OpenAsync(() => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()));
        var item = viewModel.Items[6];

        item.SelectCommand.Execute(InspectionResult.Fail);

        Assert.True(item.ShowsFaultField);
        Assert.False(item.CanAddNote);
        Assert.Equal(1, item.FaultFocusRequest);
        Assert.Null(item.FaultError);
        harness.ScreenReader.DidNotReceiveWithAnyArgs().Announce(default!);
    }

    [Theory]
    [InlineData(InspectionResult.Pass, "Pass. 7 of 10 answered.")]
    [InlineData(InspectionResult.NotApplicable, "N/A. 7 of 10 answered.")]
    public async Task PassAndNotApplicableAnnounceTheResultAndTheCount(InspectionResult result, string announcement)
    {
        var (viewModel, harness) = await OpenAsync(() => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()));

        viewModel.Items[6].SelectCommand.Execute(result);

        harness.ScreenReader.Received(1).Announce(announcement);
    }

    [Fact]
    public async Task TheNoteIsOneValueAcrossResultsAndAnOptionalBlankNoteIsSentAsNull()
    {
        var (viewModel, _) = await OpenAsync(() => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()));
        var belts = viewModel.Items[5];

        belts.SelectCommand.Execute(InspectionResult.Fail);
        Assert.Equal(InspectionDtoBuilder.BeltNote, belts.Note);
        Assert.True(belts.IsComplete);
        var filters = viewModel.Items[1];
        filters.BeginNoteCommand.Execute(null);
        Assert.True(filters.ShowsNoteField);
        filters.Note = "   ";
        Assert.Null(filters.ToInput().Note);
        filters.Note = "  Replaced both filters  ";
        Assert.Equal("Replaced both filters", filters.ToInput().Note);
        filters.EndNoteCommand.Execute(null);
        Assert.True(filters.ShowsNoteBox);
    }

    [Fact]
    public async Task BackSavesOnlyTheChangedCompleteAnswersWithTheVersionAndThenLeaves()
    {
        var saved = InspectionDtoBuilder.WithAnswers(InspectionDtoBuilder.Draft1042(), InspectionDtoBuilder.Version + 1, (7, InspectionDtoBuilder.Answer(InspectionResult.Pass)));
        var (viewModel, harness) = await OpenAsync(
            () => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()),
            () => InspectionDtoBuilder.Json(saved));
        viewModel.Items[6].SelectCommand.Execute(InspectionResult.Pass);
        viewModel.Items[8].SelectCommand.Execute(InspectionResult.Fail);
        Assert.Equal(2, harness.Store.UnsavedCount(JobDtoBuilder.Job1042));

        await viewModel.BackCommand.ExecuteAsync(null);

        var put = Assert.Single(harness.Stub.Requests, request => request.Method == HttpMethod.Put);
        Assert.Equal("/api/jobs/" + JobDtoBuilder.Job1042.ToString("D") + "/inspection", put.Path);
        var body = ProblemResponses.ReadBody(put);
        Assert.Equal(InspectionDtoBuilder.Version, body.GetProperty("version").GetUInt32());
        var answer = Assert.Single(body.GetProperty("answers").EnumerateArray());
        Assert.Equal(InspectionDtoBuilder.ItemId(7), answer.GetProperty("checklistItemId").GetGuid());
        Assert.Equal("Pass", answer.GetProperty("result").GetString());
        Assert.Equal(JsonValueKind.Null, answer.GetProperty("note").ValueKind);
        await harness.Navigator.Received(1).GoToAsync(Routes.Back);
        Assert.Equal(1, harness.Store.UnsavedCount(JobDtoBuilder.Job1042));
        Assert.Contains(InspectionDtoBuilder.ItemId(9), harness.Store.Answers(JobDtoBuilder.Job1042).Keys);
    }

    [Fact]
    public async Task BackWithNothingChangedLeavesAtOnceWithoutARequest()
    {
        var (viewModel, harness) = await OpenAsync(() => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()));

        await viewModel.BackCommand.ExecuteAsync(null);

        Assert.Single(harness.Stub.Requests);
        await harness.Navigator.Received(1).GoToAsync(Routes.Back);
    }

    [Fact]
    public async Task ReviewWithAnIncompleteAnswerShowsItsErrorFocusesItAndAnnouncesTheCount()
    {
        var (viewModel, harness) = await OpenAsync(() => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()));
        var drain = viewModel.Items[6];
        var supply = viewModel.Items[7];
        drain.SelectCommand.Execute(InspectionResult.Fail);
        supply.SelectCommand.Execute(InspectionResult.Pass);
        supply.Readings[0].Text = "54.555";

        await viewModel.ReviewCommand.ExecuteAsync(null);

        Assert.DoesNotContain(harness.Stub.Requests, request => request.Method == HttpMethod.Put);
        Assert.Equal(InspectionItemViewModel.FaultRequiredMessage, drain.FaultError);
        Assert.Equal(ReadingValue.TooManyDecimalsMessage, supply.Readings[0].Error);
        Assert.Equal(1, drain.ScrollRequest);
        Assert.Equal(2, drain.FaultFocusRequest);
        harness.ScreenReader.Received(1).Announce("2 answers need more detail.");
        await harness.Navigator.DidNotReceiveWithAnyArgs().GoToAsync(default!, default!);
        drain.Note = "Drain line blocked at the trap";
        Assert.Null(drain.FaultError);
        supply.Readings[0].Text = "54.5";
        Assert.Null(supply.Readings[0].Error);
    }

    [Fact]
    public async Task ReviewWithUnansweredItemsFocusesTheFirstAndAnnouncesHowManyAreLeft()
    {
        var (viewModel, harness) = await OpenAsync(() => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()));

        await viewModel.ReviewCommand.ExecuteAsync(null);

        Assert.Equal(1, viewModel.Items[6].PromptFocusRequest);
        Assert.Equal(1, viewModel.Items[6].ScrollRequest);
        Assert.Null(viewModel.Items[6].FaultError);
        harness.ScreenReader.Received(1).Announce("4 items left to answer.");
        Assert.Equal(AnswerOutcome.Unanswered, viewModel.AnswerOutcome);
    }

    [Fact]
    public async Task ReviewSavesAndOpensReviewAndSubmitWhenEveryItemIsAnswered()
    {
        var complete = InspectionDtoBuilder.Complete1042();
        var (viewModel, harness) = await OpenAsync(
            () => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()),
            () => InspectionDtoBuilder.Json(complete));
        foreach (var item in viewModel.Items.Skip(6))
        {
            item.SelectCommand.Execute(InspectionResult.Pass);
        }

        viewModel.Items[7].Readings[0].Text = "54";

        await viewModel.ReviewCommand.ExecuteAsync(null);

        var body = ProblemResponses.ReadBody(harness.Stub.Requests.Single(request => request.Method == HttpMethod.Put));
        Assert.Equal(4, body.GetProperty("answers").GetArrayLength());
        Assert.Equal(54, body.GetProperty("answers")[1].GetProperty("readings")[0].GetProperty("value").GetDecimal());
        await harness.Navigator.Received(1).GoToAsync(Routes.ReviewPage, Arg.Is<IDictionary<string, object>>(parameters =>
            (Guid)parameters[Routes.JobIdParameter] == JobDtoBuilder.Job1042 && (string)parameters[Routes.SourceParameter] == Routes.FromInspection));
        Assert.Equal(complete.Version, harness.Store.Loaded(JobDtoBuilder.Job1042)!.Inspection.Version);
        Assert.Equal(0, harness.Store.UnsavedCount(JobDtoBuilder.Job1042));
    }

    [Fact]
    public async Task FieldErrorsKeyedByRequestIndexMapBackToTheAnswersSentAtThatIndex()
    {
        var (viewModel, harness) = await OpenAsync(
            () => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()),
            () => ProblemResponses.ValidationFailed(new Dictionary<string, string[]>
            {
                ["answers[1].note"] = ["Describe the problem for a failed item."],
                ["answers[0].readings[1].value"] = ["Use a value between -99999999.99 and 99999999.99 with at most two decimal places."],
            }));
        var pressures = viewModel.Items[2];
        pressures.Readings[1].Text = "290";
        var drain = viewModel.Items[6];
        drain.SelectCommand.Execute(InspectionResult.Fail);
        drain.Note = "Blocked";

        await viewModel.BackCommand.ExecuteAsync(null);

        Assert.Equal("Describe the problem for a failed item.", drain.FaultError);
        Assert.Equal("Use a value between -99999999.99 and 99999999.99 with at most two decimal places.", pressures.Readings[1].Error);
        Assert.Null(pressures.Readings[0].Error);
        Assert.Null(viewModel.SlotBanner);
        Assert.Equal(1, pressures.ScrollRequest);
        await harness.Navigator.DidNotReceive().GoToAsync(Routes.Back);
        Assert.Equal(2, harness.Store.UnsavedCount(JobDtoBuilder.Job1042));
    }

    [Fact]
    public async Task ANoteErrorOnAnAnswerThatIsNotFailOpensTheNoteField()
    {
        var (viewModel, _) = await OpenAsync(
            () => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()),
            () => ProblemResponses.ValidationFailed(new Dictionary<string, string[]> { ["answers[0].note"] = ["The note is too long."] }));
        var item = viewModel.Items[6];
        item.SelectCommand.Execute(InspectionResult.Pass);
        item.Note = "Clear";

        await viewModel.BackCommand.ExecuteAsync(null);

        Assert.True(item.ShowsNoteField);
        Assert.Equal("The note is too long.", item.NoteError);
        Assert.Equal(1, item.NoteFocusRequest);
    }

    [Theory]
    [InlineData("answers")]
    [InlineData("version")]
    [InlineData("answers[0].checklistItemId")]
    [InlineData("answers[0].readings[0].readingFieldId")]
    [InlineData("answers[5].note")]
    public async Task AFieldErrorTheAppCannotPlaceShowsTheSaveBannerWithItsReference(string key)
    {
        var (viewModel, harness) = await OpenAsync(
            () => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()),
            () => ProblemResponses.ValidationFailed(new Dictionary<string, string[]> { [key] = ["Choose an item of this job's checklist."] }));
        viewModel.Items[6].SelectCommand.Execute(InspectionResult.Pass);

        await viewModel.BackCommand.ExecuteAsync(null);

        Assert.Equal(BannerVariant.Error, viewModel.SlotBanner?.Variant);
        Assert.Equal("Your answers could not be saved.", viewModel.SlotBanner?.Title);
        Assert.Equal("Try again in a moment.", viewModel.SlotBanner?.Message);
        Assert.NotNull(viewModel.SlotBanner?.Reference);
        Assert.Equal(1, viewModel.ScrollToTopRequest);
        await harness.Navigator.DidNotReceive().GoToAsync(Routes.Back);
    }

    [Fact]
    public async Task AConcurrencyConflictReloadsKeepsTheTechniciansChangesAndUsesTheNewVersion()
    {
        var newer = InspectionDtoBuilder.WithAnswers(InspectionDtoBuilder.Draft1042(), InspectionDtoBuilder.Version + 5, (2, InspectionDtoBuilder.Answer(InspectionResult.Fail, "Filters missing")));
        var after = InspectionDtoBuilder.WithAnswers(newer, InspectionDtoBuilder.Version + 6, (7, InspectionDtoBuilder.Answer(InspectionResult.Pass)));
        var (viewModel, harness) = await OpenAsync(
            () => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()),
            InspectionDtoBuilder.Conflict,
            () => InspectionDtoBuilder.Json(newer),
            () => InspectionDtoBuilder.Json(after));
        viewModel.Items[6].SelectCommand.Execute(InspectionResult.Pass);

        await viewModel.BackCommand.ExecuteAsync(null);

        Assert.Equal(BannerVariant.Conflict, viewModel.SlotBanner?.Variant);
        Assert.Equal("JOB-1042 changed after you opened it.", viewModel.SlotBanner?.Title);
        Assert.Equal("The latest answers are shown with your changes. Check them, then try again.", viewModel.SlotBanner?.Message);
        Assert.True(viewModel.Items[1].IsFail);
        Assert.Equal("Filters missing", viewModel.Items[1].Note);
        Assert.True(viewModel.Items[6].IsPass);
        await harness.Navigator.DidNotReceive().GoToAsync(Routes.Back);

        await viewModel.BackCommand.ExecuteAsync(null);

        var retry = harness.Stub.Requests.Where(request => request.Method == HttpMethod.Put).Last();
        Assert.Equal(InspectionDtoBuilder.Version + 5, ProblemResponses.ReadBody(retry).GetProperty("version").GetUInt32());
        await harness.Navigator.Received(1).GoToAsync(Routes.Back);
    }

    [Fact]
    public async Task ALockedSaveTurnsTheChecklistReadOnlyWithTheRefusedBannerAndClearsTheDraft()
    {
        var submitted = InspectionDtoBuilder.Complete1042(canEdit: false, status: JobStatus.Completed, submittedAt: JobDtoBuilder.Local(10, 19, 10, 58));
        var (viewModel, harness) = await OpenAsync(
            () => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()),
            InspectionDtoBuilder.Locked,
            () => InspectionDtoBuilder.Json(submitted));
        viewModel.Items[6].SelectCommand.Execute(InspectionResult.Fail);
        viewModel.Items[7].SelectCommand.Execute(InspectionResult.NotApplicable);

        await viewModel.BackCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsEditable);
        Assert.False(viewModel.ShowsDock);
        Assert.Equal(BannerVariant.Locked, viewModel.SlotBanner?.Variant);
        Assert.Equal("Your changes were not saved.", viewModel.SlotBanner?.Title);
        Assert.Equal("This checklist is read-only because the job was submitted at 10:58 AM.", viewModel.SlotBanner?.Message);
        Assert.Equal(0, harness.Store.UnsavedCount(JobDtoBuilder.Job1042));
        Assert.All(viewModel.Items, item => Assert.True(item.IsReadOnly));
        await harness.Navigator.DidNotReceive().GoToAsync(Routes.Back);
    }

    [Fact]
    public async Task ASaveLockedBecauseTheJobWentOnHoldDropsTheUnsavedChangesAndShowsTheSavedAnswers()
    {
        var (viewModel, harness) = await OpenAsync(
            () => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()),
            InspectionDtoBuilder.Locked,
            () => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042(canEdit: false, status: JobStatus.OnHold)));
        viewModel.Items[6].SelectCommand.Execute(InspectionResult.NotApplicable);

        await viewModel.BackCommand.ExecuteAsync(null);

        Assert.Equal("Your changes were not saved.", viewModel.SlotBanner?.Title);
        Assert.Equal("This checklist is read-only because the job is on hold. Resume the job to change it.", viewModel.SlotBanner?.Message);
        Assert.Equal(0, harness.Store.UnsavedCount(JobDtoBuilder.Job1042));
        Assert.Empty(harness.Store.Answers(JobDtoBuilder.Job1042));
        Assert.Null(viewModel.Items[6].Result);
        Assert.True(viewModel.Items[6].ShowsNotAnswered);
    }

    [Theory]
    [InlineData("rate-limited", "They are kept on this device. Try again in 30 seconds.", false)]
    [InlineData("unreachable", "They are kept on this device. Check your connection, then try again.", false)]
    [InlineData("server", "They are kept on this device. Try again in a moment.", true)]
    [InlineData("forbidden", "They are kept on this device. Try again in a moment.", true)]
    public async Task ASaveThatFailsKeepsTheAnswersShowsTheBannerAndTheNextBackLeavesWithoutARequest(string failure, string message, bool hasReference)
    {
        Func<HttpResponseMessage> response = failure switch
        {
            "rate-limited" => () => ProblemResponses.RateLimited(TimeSpan.FromSeconds(30)),
            "unreachable" => () => throw new HttpRequestException("unreachable"),
            "forbidden" => () => ProblemResponses.Problem(HttpStatusCode.Forbidden, "auth.forbidden", "Forbidden."),
            _ => () => ProblemResponses.ServerError(Guid.NewGuid().ToString("D")),
        };
        var (viewModel, harness) = await OpenAsync(() => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()), response);
        viewModel.Items[6].SelectCommand.Execute(InspectionResult.Pass);

        await viewModel.BackCommand.ExecuteAsync(null);

        Assert.Equal("Your answers could not be saved.", viewModel.SlotBanner?.Title);
        Assert.Equal(message, viewModel.SlotBanner?.Message);
        Assert.Equal(hasReference, viewModel.SlotBanner?.Reference is not null);
        Assert.Equal(1, harness.Store.UnsavedCount(JobDtoBuilder.Job1042));
        await harness.Navigator.DidNotReceive().GoToAsync(Routes.Back);

        await viewModel.BackCommand.ExecuteAsync(null);

        Assert.Single(harness.Stub.Requests, request => request.Method == HttpMethod.Put);
        await harness.Navigator.Received(1).GoToAsync(Routes.Back);
        Assert.Equal(1, harness.Store.UnsavedCount(JobDtoBuilder.Job1042));
    }

    [Fact]
    public async Task AJobThatIsNoLongerAssignedReturnsToMyJobsWithTheNotice()
    {
        var (_, harness) = await OpenAsync(ProblemResponses.NotFound);

        await harness.Navigator.Received(1).GoToAsync(Routes.Jobs, Arg.Is<IDictionary<string, object>>(parameters =>
            (string)parameters[Routes.NoticeParameter] == Routes.JobUnassignedNotice));
    }

    [Fact]
    public async Task OfflineTheAnswersStayTheReviewIsDisabledAndBackLeavesAtOnce()
    {
        var (viewModel, harness) = await OpenAsync(() => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()));

        harness.SetOnline(false);
        viewModel.Items[6].SelectCommand.Execute(InspectionResult.Pass);

        Assert.True(viewModel.ShowsOfflineBanner);
        Assert.Equal("You're offline. Connect to review and submit.", viewModel.HelperText);
        Assert.False(viewModel.ReviewCommand.CanExecute(null));
        await viewModel.BackCommand.ExecuteAsync(null);
        Assert.Single(harness.Stub.Requests);
        await harness.Navigator.Received(1).GoToAsync(Routes.Back);
        Assert.Equal(1, harness.Store.UnsavedCount(JobDtoBuilder.Job1042));
        harness.SetOnline(true);
        Assert.False(viewModel.ShowsOfflineBanner);
        Assert.Equal("3 items left to answer", viewModel.HelperText);
        Assert.True(viewModel.ReviewCommand.CanExecute(null));
    }

    [Fact]
    public async Task ReopeningLaysTheDraftOverTheCopyLoadedBeforeAndShowsIncompleteErrorsAtOnce()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()),
            () => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()),
            () => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042())));
        var first = await OpenAsync(harness);
        first.Items[6].SelectCommand.Execute(InspectionResult.Fail);
        first.Items[8].SelectCommand.Execute(InspectionResult.NotApplicable);
        harness.SetOnline(false);
        await first.BackCommand.ExecuteAsync(null);
        harness.SetOnline(true);

        var second = await OpenAsync(harness);

        Assert.True(second.Items[6].IsFail);
        Assert.Equal(InspectionItemViewModel.FaultRequiredMessage, second.Items[6].FaultError);
        Assert.True(second.Items[8].IsNotApplicable);
        Assert.Equal("8", second.AnsweredText);
        Assert.Equal(2, harness.Stub.Requests.Count);
    }

    [Fact]
    public async Task OpeningAgainShowsTheLoadedCopyAtOnceAndAFailedReloadKeepsItUnderTheRefreshBanner()
    {
        var correlationId = Guid.NewGuid().ToString("D");
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()),
            () => ProblemResponses.ServerError(correlationId)));
        await OpenAsync(harness);

        var second = await OpenAsync(harness);

        Assert.Equal(ScreenState.Content, second.State);
        Assert.Equal("6", second.AnsweredText);
        Assert.True(second.HasRefreshError);
        Assert.Equal("This checklist could not be refreshed.", second.RefreshError?.Title);
        Assert.Equal("Showing it as loaded at 10:42 AM.", second.RefreshError?.Message);
        Assert.Equal(correlationId, second.RefreshError?.Reference);
    }

    [Fact]
    public async Task AReadOnlyItemShowsItsReadingWellsOnlyWhenItHasSavedReadings()
    {
        var submitted = InspectionDtoBuilder.Complete1042(canEdit: false, status: JobStatus.Completed, submittedAt: JobDtoBuilder.Local(10, 19, 10, 58));
        var (readOnly, _) = await OpenAsync(() => InspectionDtoBuilder.Json(submitted));
        var (editable, _) = await OpenAsync(() => InspectionDtoBuilder.Json(InspectionDtoBuilder.Complete1042()));

        Assert.False(readOnly.Items[0].ShowsSavedReadings);
        Assert.False(readOnly.Items[4].ShowsSavedReadings);
        Assert.True(readOnly.Items[2].ShowsSavedReadings);
        Assert.True(readOnly.Items[7].ShowsSavedReadings);
        Assert.All(editable.Items, item => Assert.False(item.ShowsSavedReadings));
        Assert.All(readOnly.Items, item => Assert.False(item.IsEditable || item.ShowsNoteField || item.ShowsFaultField || item.ShowsEditableReadings));
    }

    [Fact]
    public async Task ASaveFailureAfterARefreshFailureLeavesOneEventBanner()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()),
            () => ProblemResponses.ServerError(Guid.NewGuid().ToString("D")),
            () => ProblemResponses.ServerError(Guid.NewGuid().ToString("D"))));
        await OpenAsync(harness);
        var viewModel = await OpenAsync(harness);
        Assert.True(viewModel.HasRefreshError);
        viewModel.Items[6].SelectCommand.Execute(InspectionResult.Pass);

        await viewModel.BackCommand.ExecuteAsync(null);

        Assert.Equal(BannerVariant.Error, viewModel.SlotBanner?.Variant);
        Assert.Equal(InspectionViewModel.SaveFailedTitle, viewModel.SlotBanner?.Title);
        Assert.Null(viewModel.RefreshError);
        Assert.False(viewModel.HasRefreshError);
    }

    [Fact]
    public async Task ARefreshFailureReplacesAnEarlierEventBanner()
    {
        var (viewModel, _) = await OpenAsync(
            () => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()),
            () => ProblemResponses.ServerError(Guid.NewGuid().ToString("D")),
            () => ProblemResponses.ServerError(Guid.NewGuid().ToString("D")));
        viewModel.Items[6].SelectCommand.Execute(InspectionResult.Pass);
        await viewModel.BackCommand.ExecuteAsync(null);
        Assert.Equal(InspectionViewModel.SaveFailedTitle, viewModel.SlotBanner?.Title);

        await viewModel.AppearingCommand.ExecuteAsync(null);
        await WaitForAsync(() => viewModel.HasRefreshError);

        Assert.True(viewModel.HasRefreshError);
        Assert.Null(viewModel.SlotBanner);
    }

    [Fact]
    public async Task TheLockedNoticeStaysUnderARefreshFailure()
    {
        var submitted = InspectionDtoBuilder.Complete1042(canEdit: false, status: JobStatus.Completed, submittedAt: JobDtoBuilder.Local(10, 19, 10, 58));
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => InspectionDtoBuilder.Json(submitted),
            () => ProblemResponses.ServerError(Guid.NewGuid().ToString("D"))));
        await OpenAsync(harness);

        var viewModel = await OpenAsync(harness);
        await WaitForAsync(() => viewModel.HasRefreshError);

        Assert.True(viewModel.HasRefreshError);
        Assert.Equal(BannerVariant.Locked, viewModel.SlotBanner?.Variant);
        Assert.True(InspectionViewModel.IsEventBanner(new PageBanner(BannerVariant.Conflict, "t", "m", null)));
        Assert.False(InspectionViewModel.IsEventBanner(new PageBanner(BannerVariant.Locked, "t", "m", null)));
        Assert.False(InspectionViewModel.IsEventBanner(null));
    }

    [Fact]
    public async Task AFirstLoadThatFailsShowsTheErrorAndTryAgainMovesFocusToTheFirstPrompt()
    {
        var (viewModel, _) = await OpenAsync(
            () => throw new HttpRequestException("unreachable"),
            () => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()));

        Assert.Equal(ScreenState.Error, viewModel.State);
        Assert.Equal("The checklist could not be loaded.", viewModel.Error?.Title);
        Assert.Equal("Check your connection, then try again.", viewModel.Error?.Message);
        Assert.Null(viewModel.Error?.Reference);
        Assert.False(viewModel.ShowsDock);
        Assert.Equal("Rooftop HVAC service", viewModel.TemplateName);

        await viewModel.RetryCommand.ExecuteAsync(null);

        Assert.Equal(ScreenState.Content, viewModel.State);
        Assert.Equal(1, viewModel.Items[0].PromptFocusRequest);
    }

    [Theory]
    [InlineData(JobStatus.OnHold, "This job is on hold.", "Resume the job to change the checklist.")]
    [InlineData(JobStatus.Cancelled, "This job was cancelled.", "The checklist is read-only.")]
    public async Task AChecklistThatCannotBeEditedIsReadOnlyWithTheLockedBanner(JobStatus status, string title, string message)
    {
        var (viewModel, _) = await OpenAsync(() => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042(canEdit: false, status: status)));

        Assert.False(viewModel.IsEditable);
        Assert.False(viewModel.ShowsDock);
        Assert.Equal(BannerVariant.Locked, viewModel.SlotBanner?.Variant);
        Assert.Equal((title, message), (viewModel.SlotBanner?.Title, viewModel.SlotBanner?.Message));
        Assert.True(viewModel.Items[0].ShowsResultChip);
        Assert.True(viewModel.Items[6].ShowsNotAnswered);
        Assert.Equal("6", viewModel.AnsweredText);
        Assert.Equal("Item 3 of 10, Check refrigerant pressures, Pass, Suction 118 psi, Discharge 285 psi", viewModel.Items[2].ReadOnlyDescription);
        Assert.Equal("Item 6 of 10, Check belts and pulleys, N/A, Direct-drive unit", viewModel.Items[5].ReadOnlyDescription);
    }

    [Fact]
    public async Task ASubmittedChecklistIsReadOnlyWithTheSubmittedMomentAndTheDraftIsCleared()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => InspectionDtoBuilder.Json(InspectionDtoBuilder.Complete1042(canEdit: false, status: JobStatus.Completed, submittedAt: JobDtoBuilder.Local(10, 16, 16, 25)))));
        harness.Store.Put(JobDtoBuilder.Job1042, InspectionDtoBuilder.ItemId(7), new DraftAnswer(InspectionResult.Fail, string.Empty, new Dictionary<Guid, string>()));

        var viewModel = await OpenAsync(harness);

        Assert.Equal("Submitted on Fri 16 Oct at 4:25 PM.", viewModel.SlotBanner?.Title);
        Assert.Equal("This checklist is read-only.", viewModel.SlotBanner?.Message);
        Assert.Equal(0, harness.Store.UnsavedCount(JobDtoBuilder.Job1042));
        Assert.True(viewModel.Items[6].IsPass);
    }

    [Fact]
    public async Task ARefusedSubmissionReturnsToTheChecklistWhichReloadsAndPointsAtTheFirstUnansweredItem()
    {
        var (viewModel, harness) = await OpenAsync(
            () => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()),
            () => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()));

        viewModel.ApplyQueryAttributes(new Dictionary<string, object>
        {
            [Routes.JobIdParameter] = JobDtoBuilder.Job1042,
            [Routes.NoticeParameter] = Routes.SubmissionIncompleteNotice,
        });
        await WaitForAsync(() => viewModel.SlotBanner is not null);

        Assert.Equal(BannerVariant.Conflict, viewModel.SlotBanner?.Variant);
        Assert.Equal("Finish the checklist first.", viewModel.SlotBanner?.Title);
        Assert.Equal("4 items are not answered yet.", viewModel.SlotBanner?.Message);
        Assert.Equal(1, viewModel.Items[6].PromptFocusRequest);
        Assert.Equal(2, harness.Stub.Requests.Count);
    }

    [Fact]
    public async Task WhileASaveRunsTheButtonReadsSavingAndTheAnswersAndBackTakeNoInput()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>();
        var calls = 0;
        var harness = new JobsHarness(new StubHttpMessageHandler((_, _) => Interlocked.Increment(ref calls) == 1
            ? Task.FromResult(InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()))
            : pending.Task));
        var viewModel = await OpenAsync(harness);
        viewModel.Items[6].SelectCommand.Execute(InspectionResult.Pass);

        var back = viewModel.BackCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSaving);
        Assert.Equal("Saving…", viewModel.ReviewLabel);
        Assert.False(viewModel.AcceptsInput);
        viewModel.Items[7].SelectCommand.Execute(InspectionResult.NotApplicable);
        Assert.Null(viewModel.Items[7].Result);
        await viewModel.BackCommand.ExecuteAsync(null);
        pending.SetResult(InspectionDtoBuilder.Json(InspectionDtoBuilder.WithAnswers(InspectionDtoBuilder.Draft1042(), InspectionDtoBuilder.Version + 1, (7, InspectionDtoBuilder.Answer(InspectionResult.Pass)))));
        await back;

        Assert.False(viewModel.IsSaving);
        Assert.Equal("Review and submit", viewModel.ReviewLabel);
        await harness.Navigator.Received(1).GoToAsync(Routes.Back);
    }

    [Fact]
    public async Task TheEndOfTheSessionClearsTheChecklistAndTheDraft()
    {
        var (viewModel, harness) = await OpenAsync(() => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()));
        viewModel.Items[6].SelectCommand.Execute(InspectionResult.Pass);

        harness.EndSession();

        Assert.Empty(viewModel.Items);
        Assert.Equal(ScreenState.Loading, viewModel.State);
        Assert.Equal(0, harness.Store.UnsavedCount(JobDtoBuilder.Job1042));
        Assert.Null(harness.Store.Loaded(JobDtoBuilder.Job1042));
    }

    [Fact]
    public async Task ReopeningAfterASaveShowsTheSavedAnswers()
    {
        var saved = InspectionDtoBuilder.WithAnswers(
            InspectionDtoBuilder.Draft1042(),
            InspectionDtoBuilder.Version + 1,
            (8, InspectionDtoBuilder.Answer(InspectionResult.Pass, null, (InspectionDtoBuilder.SupplyAir, 54.00m))));
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042()),
            () => InspectionDtoBuilder.Json(saved),
            () => InspectionDtoBuilder.Json(saved)));
        var first = await OpenAsync(harness);
        first.Items[7].SelectCommand.Execute(InspectionResult.Pass);
        first.Items[7].Readings[0].Text = "54";
        await first.BackCommand.ExecuteAsync(null);

        var second = await OpenAsync(harness);

        Assert.True(second.Items[7].IsPass);
        Assert.Equal("54", second.Items[7].Readings[0].Text);
        Assert.False(second.Items[7].IsChanged);
        Assert.Equal("7", second.AnsweredText);
    }

    [Theory]
    [InlineData("answers[3].note", 3, AnswerKeyField.Note, null)]
    [InlineData("answers[0].readings", 0, AnswerKeyField.Readings, null)]
    [InlineData("answers[12].readings[2].value", 12, AnswerKeyField.Readings, 2)]
    [InlineData("Answers[1].Note", 1, AnswerKeyField.Note, null)]
    public void AnswerKeysAreReadByRequestIndex(string key, int index, AnswerKeyField field, int? reading)
    {
        Assert.Equal(new AnswerKey(index, field, reading), InspectionViewModel.ParseAnswerKey(key));
    }

    [Theory]
    [InlineData("answers")]
    [InlineData("answers[0].checklistItemId")]
    [InlineData("answers[0].readings[1].readingFieldId")]
    [InlineData("version")]
    public void OtherKeysAreNotAnswerFieldKeys(string key)
    {
        Assert.Null(InspectionViewModel.ParseAnswerKey(key));
    }

    [Theory]
    [InlineData(3, 10, "03")]
    [InlineData(10, 10, "10")]
    [InlineData(3, 100, "003")]
    [InlineData(7, 8, "07")]
    public void ItemNumbersPadToTwoDigitsOrToTheDigitsOfTheTotal(int position, int total, string expected)
    {
        Assert.Equal(expected, InspectionItemViewModel.Number(position, total));
    }

    private static async Task<(InspectionViewModel ViewModel, JobsHarness Harness)> OpenAsync(params Func<HttpResponseMessage>[] responses)
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(responses));
        return (await OpenAsync(harness), harness);
    }

    private static async Task<InspectionViewModel> OpenAsync(JobsHarness harness)
    {
        harness.Store.RememberJob(new InspectionJob(JobDtoBuilder.Job1042, 1042, "Rooftop HVAC unit not cooling", "Rooftop HVAC service"));
        var viewModel = harness.CreateInspection();
        viewModel.ApplyQueryAttributes(JobsHarness.JobQuery(JobDtoBuilder.Job1042));
        await viewModel.AppearingCommand.ExecuteAsync(null);
        return viewModel;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
    }
}
