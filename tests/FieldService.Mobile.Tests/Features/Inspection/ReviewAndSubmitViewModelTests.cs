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

public sealed class ReviewAndSubmitViewModelTests
{
    private const string Notes = "Recharged the system to spec and cleaned the condenser coil. Unit is holding 54 °F supply air. Fan motor bearing is noisy; replacement quote requested.";

    [Fact]
    public void FromTheChecklistTheReviewUsesTheSavedInspectionWithoutARequest()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence());
        harness.Store.Remember(InspectionDtoBuilder.Complete1042(), JobDtoBuilder.Now);

        var viewModel = Open(harness, Routes.FromInspection);

        Assert.Empty(harness.Stub.Requests);
        Assert.Equal(ScreenState.Content, viewModel.State);
        Assert.Equal("JOB-1042", viewModel.JobNumber);
        Assert.Equal("Rooftop HVAC unit not cooling", viewModel.JobTitle);
        Assert.Equal("Back to inspection", viewModel.BackDescription);
        Assert.Equal(("8 Pass", "1 Fail", "1 N/A"), (viewModel.PassText, viewModel.FailText, viewModel.NotApplicableText));
        var failed = Assert.Single(viewModel.FailedItems);
        Assert.Equal("05 / 10", failed.NumberText);
        Assert.Equal("Inspect condenser fan motor and bearings", failed.Prompt);
        Assert.Equal(InspectionDtoBuilder.FanNote, failed.Note);
        Assert.False(viewModel.IsConfirmed);
        Assert.True(viewModel.ShowsDock);
        Assert.Equal("After you submit, the job moves to Completed and dispatch is notified.", viewModel.HelperText);
    }

    [Fact]
    public async Task FromTheSheetTheReviewLoadsTheInspectionAndItsBackReturnsToJobDetails()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => InspectionDtoBuilder.Json(InspectionDtoBuilder.Complete1042())));

        var viewModel = Open(harness, Routes.FromSheet);
        await WaitForAsync(() => viewModel.State == ScreenState.Content);

        Assert.Equal("Back to job details", viewModel.BackDescription);
        Assert.Equal(HttpMethod.Get, Assert.Single(harness.Stub.Requests).Method);
        Assert.Single(viewModel.FailedItems);
    }

    [Fact]
    public async Task ALoadThatFailsFromTheSheetShowsTheChecklistLoadFailureWithTryAgain()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => ProblemResponses.ServerError(Guid.NewGuid().ToString("D")),
            () => InspectionDtoBuilder.Json(InspectionDtoBuilder.Complete1042())));

        var viewModel = Open(harness, Routes.FromSheet);
        await WaitForAsync(() => viewModel.State == ScreenState.Error);

        Assert.Equal("The checklist could not be loaded.", viewModel.Error?.Title);
        Assert.False(viewModel.ShowsDock);
        await viewModel.RetryCommand.ExecuteAsync(null);
        Assert.Equal(ScreenState.Content, viewModel.State);
    }

    [Theory]
    [InlineData(19, 10, 58, "9:12–10:58 AM · 1 h 46 min")]
    [InlineData(19, 9, 12, "9:12–9:12 AM · Less than a minute")]
    [InlineData(19, 9, 57, "9:12–9:57 AM · 45 min")]
    [InlineData(19, 13, 5, "9:12 AM–1:05 PM · 3 h 53 min")]
    public void TimeOnSiteRunsFromTheStartToNow(int day, int hour, int minute, string expected)
    {
        var start = JobDtoBuilder.Local(10, 19, 9, 12);
        var now = JobDtoBuilder.Local(10, day, hour, minute);

        Assert.Equal(expected, ReviewAndSubmitViewModel.TimeOnSite(start, now, JobDtoBuilder.Zone, DateOnly.FromDateTime(now.DateTime)));
    }

    [Theory]
    [InlineData(21, 42, 50, 21, 58, 10, "9:42–9:58 PM · 16 min")]
    [InlineData(22, 16, 59, 22, 19, 1, "10:16–10:19 PM · 3 min")]
    [InlineData(21, 42, 10, 21, 42, 50, "9:42–9:42 PM · Less than a minute")]
    [InlineData(21, 42, 59, 21, 43, 0, "9:42–9:43 PM · 1 min")]
    public void TimeOnSiteIsTheDifferenceOfTheTwoShownMomentsEachTruncatedToItsMinute(int startHour, int startMinute, int startSecond, int nowHour, int nowMinute, int nowSecond, string expected)
    {
        var start = JobDtoBuilder.Local(10, 19, startHour, startMinute).AddSeconds(startSecond);
        var now = JobDtoBuilder.Local(10, 19, nowHour, nowMinute).AddSeconds(nowSecond);

        Assert.Equal(expected, ReviewAndSubmitViewModel.TimeOnSite(start, now, JobDtoBuilder.Zone, new DateOnly(2026, 10, 19)));
        Assert.Equal(0, ReviewAndSubmitViewModel.ToMinute(now).Second);
        Assert.Equal(now.Offset, ReviewAndSubmitViewModel.ToMinute(now).Offset);
    }

    [Fact]
    public void TimeOnSiteFromAnEarlierDayCarriesTheStartsDate()
    {
        var start = JobDtoBuilder.Local(10, 16, 15, 40);
        var now = JobDtoBuilder.Local(10, 19, 10, 58);

        Assert.Equal(
            "Fri 16 Oct, 3:40 PM – 10:58 AM · 67 h 18 min",
            ReviewAndSubmitViewModel.TimeOnSite(start, now, JobDtoBuilder.Zone, new DateOnly(2026, 10, 19)));
    }

    [Fact]
    public void TimeOnSiteUpdatesOnceAMinuteWhileTheScreenShows()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence());
        harness.Store.Remember(InspectionDtoBuilder.Complete1042(), JobDtoBuilder.Now);
        var viewModel = Open(harness, Routes.FromInspection);
        viewModel.AppearingCommand.Execute(null);
        var before = viewModel.TimeOnSiteText;

        harness.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal("9:12–10:42 AM · 1 h 30 min", before);
        Assert.Equal("9:12–10:43 AM · 1 h 31 min", viewModel.TimeOnSiteText);
    }

    [Fact]
    public async Task SubmittingWithoutTheConfirmationSendsNothingAndShowsItsError()
    {
        var harness = Ready(out var viewModel);

        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Empty(harness.Stub.Requests);
        Assert.Equal("Confirm the site is safe before you submit.", viewModel.ConfirmationError);
        Assert.Equal(1, viewModel.ConfirmationFocusRequest);
        Assert.True(viewModel.SubmitCommand.CanExecute(null));
        viewModel.IsConfirmed = true;
        Assert.Null(viewModel.ConfirmationError);
    }

    [Fact]
    public async Task ASubmissionSendsTheTrimmedNotesTheConfirmationAndTheVersionThenOpensMyJobsWithTheSuccessNotice()
    {
        var submitted = JobDtoBuilder.Details1042() with { Status = JobStatus.Completed };
        var harness = Ready(out var viewModel, () => JobDtoBuilder.Json(submitted));
        harness.Store.Put(JobDtoBuilder.Job1042, InspectionDtoBuilder.ItemId(7), new DraftAnswer(InspectionResult.Fail, string.Empty, new Dictionary<Guid, string>()));
        viewModel.TechnicianNotes = "  " + Notes + "  ";
        viewModel.IsConfirmed = true;

        await viewModel.SubmitCommand.ExecuteAsync(null);

        var post = Assert.Single(harness.Stub.Requests);
        Assert.Equal(HttpMethod.Post, post.Method);
        Assert.Equal("/api/jobs/" + JobDtoBuilder.Job1042.ToString("D") + "/submission", post.Path);
        var body = ProblemResponses.ReadBody(post);
        Assert.Equal(Notes, body.GetProperty("technicianNotes").GetString());
        Assert.True(body.GetProperty("siteSafetyConfirmed").GetBoolean());
        Assert.Equal(InspectionDtoBuilder.Version + 1, body.GetProperty("version").GetUInt32());
        await harness.Navigator.Received(1).GoToAsync(Routes.Jobs, Arg.Is<IDictionary<string, object>>(parameters =>
            (string)parameters[Routes.NoticeParameter] == Routes.JobSubmittedNotice && (string)parameters[Routes.JobNumberParameter] == "JOB-1042"));
        Assert.Equal(0, harness.Store.UnsavedCount(JobDtoBuilder.Job1042));
        Assert.Equal(string.Empty, harness.Store.Notes(JobDtoBuilder.Job1042));
    }

    [Fact]
    public async Task BlankNotesAreSentAsNull()
    {
        var harness = Ready(out var viewModel, () => JobDtoBuilder.Json(JobDtoBuilder.Details1042()));
        viewModel.TechnicianNotes = "   ";
        viewModel.IsConfirmed = true;

        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(JsonValueKind.Null, ProblemResponses.ReadBody(harness.Stub.Requests[0]).GetProperty("technicianNotes").ValueKind);
    }

    [Fact]
    public void TheNotesStayInTheDraftAndTheConfirmationStartsUncheckedWheneverTheScreenOpens()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence());
        harness.Store.Remember(InspectionDtoBuilder.Complete1042(), JobDtoBuilder.Now);
        harness.Store.RememberJob(new InspectionJob(JobDtoBuilder.Job1042, 1042, "Rooftop HVAC unit not cooling", "Rooftop HVAC service"));
        var first = harness.CreateReview();
        first.ApplyQueryAttributes(Query(Routes.FromInspection));
        first.TechnicianNotes = Notes;
        first.IsConfirmed = true;

        var second = harness.CreateReview();
        second.ApplyQueryAttributes(Query(Routes.FromInspection));
        first.ApplyQueryAttributes(Query(Routes.FromInspection));

        Assert.Equal(Notes, second.TechnicianNotes);
        Assert.False(second.IsConfirmed);
        Assert.Equal(Notes, first.TechnicianNotes);
        Assert.False(first.IsConfirmed);
    }

    [Theory]
    [InlineData("siteSafetyConfirmed", "Confirm that the site is safe.")]
    [InlineData("technicianNotes", "Use technician notes of 2000 characters or fewer.")]
    public async Task AFieldErrorFromTheServerShowsOnItsField(string key, string message)
    {
        var harness = Ready(out var viewModel, () => ProblemResponses.ValidationFailed(new Dictionary<string, string[]> { [key] = [message] }));
        viewModel.IsConfirmed = true;

        await viewModel.SubmitCommand.ExecuteAsync(null);

        if (key == ReviewAndSubmitViewModel.SiteSafetyKey)
        {
            Assert.Equal(message, viewModel.ConfirmationError);
            Assert.Equal(1, viewModel.ConfirmationFocusRequest);
        }
        else
        {
            Assert.Equal(message, viewModel.NotesError);
            Assert.Equal(1, viewModel.NotesFocusRequest);
        }

        Assert.Null(viewModel.SlotBanner);
        await harness.Navigator.DidNotReceiveWithAnyArgs().GoToAsync(default!, default!);
    }

    [Theory]
    [InlineData(Routes.FromInspection, Routes.Back)]
    [InlineData(Routes.FromSheet, Routes.BackToInspection)]
    public async Task AnIncompleteChecklistReturnsToTheChecklistWithTheRefusalNotice(string source, string route)
    {
        var harness = Ready(out var viewModel, InspectionDtoBuilder.Incomplete, source);
        await WaitForAsync(() => viewModel.State == ScreenState.Content);
        viewModel.IsConfirmed = true;

        await viewModel.SubmitCommand.ExecuteAsync(null);

        await harness.Navigator.Received(1).GoToAsync(route, Arg.Is<IDictionary<string, object>>(parameters =>
            (string)parameters[Routes.NoticeParameter] == Routes.SubmissionIncompleteNotice && (Guid)parameters[Routes.JobIdParameter] == JobDtoBuilder.Job1042));
    }

    [Theory]
    [InlineData(Routes.FromInspection, Routes.BackTwo)]
    [InlineData(Routes.FromSheet, Routes.Back)]
    public async Task AnInvalidTransitionReturnsToJobDetailsWithItsNotice(string source, string route)
    {
        var harness = Ready(out var viewModel, InspectionDtoBuilder.InvalidTransition, source);
        await WaitForAsync(() => viewModel.State == ScreenState.Content);
        viewModel.IsConfirmed = true;

        await viewModel.SubmitCommand.ExecuteAsync(null);

        await harness.Navigator.Received(1).GoToAsync(route, Arg.Is<IDictionary<string, object>>(parameters =>
            (string)parameters[Routes.NoticeParameter] == Routes.SubmissionInvalidTransitionNotice));
    }

    [Fact]
    public async Task TheUncheckedPressAndTheServersSafetyErrorBothAskToRevealTheConfirmationRow()
    {
        var harness = Ready(out var viewModel, () => ProblemResponses.ValidationFailed(new Dictionary<string, string[]> { [ReviewAndSubmitViewModel.SiteSafetyKey] = ["Confirm that the site is safe."] }));

        await viewModel.SubmitCommand.ExecuteAsync(null);
        var afterPress = viewModel.ConfirmationFocusRequest;
        viewModel.IsConfirmed = true;
        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(1, afterPress);
        Assert.Equal(2, viewModel.ConfirmationFocusRequest);
        Assert.Equal("Confirm that the site is safe.", viewModel.ConfirmationError);
        Assert.False(viewModel.IsConfirmed);
        Assert.Single(harness.Stub.Requests);
    }

    [Fact]
    public async Task ALockedSubmissionOfAJobThatIsNowCompletedEndsAsASubmission()
    {
        var completed = JobDtoBuilder.Details1042() with { Status = JobStatus.Completed, Affordances = new JobAffordances([], false) };
        var harness = Ready(out var viewModel, InspectionDtoBuilder.Locked, () => JobDtoBuilder.Json(completed));
        harness.Store.Put(JobDtoBuilder.Job1042, InspectionDtoBuilder.ItemId(7), new DraftAnswer(InspectionResult.Fail, string.Empty, new Dictionary<Guid, string>()));
        harness.Store.SetNotes(JobDtoBuilder.Job1042, Notes);
        viewModel.IsConfirmed = true;

        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal([HttpMethod.Post, HttpMethod.Get], harness.Stub.Requests.Select(request => request.Method));
        Assert.Equal("/api/jobs/" + JobDtoBuilder.Job1042.ToString("D"), harness.Stub.Requests[1].Path);
        await harness.Navigator.Received(1).GoToAsync(Routes.Jobs, Arg.Is<IDictionary<string, object>>(parameters =>
            (string)parameters[Routes.NoticeParameter] == Routes.JobSubmittedNotice && (string)parameters[Routes.JobNumberParameter] == "JOB-1042"));
        Assert.Equal(0, harness.Store.UnsavedCount(JobDtoBuilder.Job1042));
        Assert.Equal(string.Empty, harness.Store.Notes(JobDtoBuilder.Job1042));
        Assert.Null(viewModel.SlotBanner);
    }

    [Theory]
    [InlineData(JobStatus.OnHold, Routes.FromInspection, Routes.BackTwo)]
    [InlineData(JobStatus.Cancelled, Routes.FromSheet, Routes.Back)]
    public async Task ALockedSubmissionOfAJobThatIsNotCompletedReturnsToJobDetailsAsAnInvalidTransition(JobStatus status, string source, string route)
    {
        var job = JobDtoBuilder.Details1042() with { Status = status, Affordances = new JobAffordances([], false) };
        var harness = Ready(out var viewModel, InspectionDtoBuilder.Locked, () => JobDtoBuilder.Json(job), source);
        await WaitForAsync(() => viewModel.State == ScreenState.Content);
        harness.Store.SetNotes(JobDtoBuilder.Job1042, Notes);
        viewModel.IsConfirmed = true;

        await viewModel.SubmitCommand.ExecuteAsync(null);

        await harness.Navigator.Received(1).GoToAsync(route, Arg.Is<IDictionary<string, object>>(parameters =>
            (string)parameters[Routes.NoticeParameter] == Routes.SubmissionInvalidTransitionNotice));
        await harness.Navigator.DidNotReceive().GoToAsync(Routes.Jobs, Arg.Any<IDictionary<string, object>>());
        Assert.Equal(Notes, harness.Store.Notes(JobDtoBuilder.Job1042));
    }

    [Fact]
    public async Task ALockedSubmissionWhoseJobCannotBeReloadedShowsTheSubmitFailure()
    {
        var harness = Ready(out var viewModel, InspectionDtoBuilder.Locked, () => throw new HttpRequestException("unreachable"));
        viewModel.IsConfirmed = true;

        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(BannerVariant.Error, viewModel.SlotBanner?.Variant);
        Assert.Equal("The job could not be submitted.", viewModel.SlotBanner?.Title);
        Assert.Equal("Check your connection, then try again.", viewModel.SlotBanner?.Message);
        await harness.Navigator.DidNotReceiveWithAnyArgs().GoToAsync(default!, default!);
        Assert.False(viewModel.IsSubmitting);
    }

    [Fact]
    public async Task AConflictReloadsTheInspectionHereAndKeepsTheNotesTheBoxAndTheFocus()
    {
        var harness = Ready(
            out var viewModel,
            InspectionDtoBuilder.Conflict,
            () => InspectionDtoBuilder.Json(InspectionDtoBuilder.Complete1042(version: InspectionDtoBuilder.Version + 9)));
        viewModel.TechnicianNotes = Notes;
        viewModel.IsConfirmed = true;

        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(BannerVariant.Conflict, viewModel.SlotBanner?.Variant);
        Assert.Equal("JOB-1042 changed after you opened it.", viewModel.SlotBanner?.Title);
        Assert.Equal("The latest version is shown. Check it, then try again.", viewModel.SlotBanner?.Message);
        Assert.Equal(Notes, viewModel.TechnicianNotes);
        Assert.True(viewModel.IsConfirmed);
        Assert.Equal(1, viewModel.SubmitFocusRequest);
        Assert.Equal(InspectionDtoBuilder.Version + 9, viewModel.Inspection?.Version);
    }

    [Theory]
    [InlineData("rate-limited", "Try again in 30 seconds.", false)]
    [InlineData("unreachable", "Check your connection, then try again.", false)]
    [InlineData("server", "Try again in a moment.", true)]
    [InlineData("invalid", "Try again in a moment.", true)]
    public async Task ASubmissionThatFailsShowsTheBanner(string failure, string message, bool hasReference)
    {
        Func<HttpResponseMessage> response = failure switch
        {
            "rate-limited" => () => ProblemResponses.RateLimited(TimeSpan.FromSeconds(30)),
            "unreachable" => () => throw new HttpRequestException("unreachable"),
            "invalid" => () => ProblemResponses.ValidationFailed(new Dictionary<string, string[]> { ["version"] = ["The version is required."] }),
            _ => () => ProblemResponses.ServerError(Guid.NewGuid().ToString("D")),
        };
        var harness = Ready(out var viewModel, response);
        viewModel.IsConfirmed = true;

        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(BannerVariant.Error, viewModel.SlotBanner?.Variant);
        Assert.Equal("The job could not be submitted.", viewModel.SlotBanner?.Title);
        Assert.Equal(message, viewModel.SlotBanner?.Message);
        Assert.Equal(hasReference, viewModel.SlotBanner?.Reference is not null);
        Assert.Equal(1, viewModel.ScrollToTopRequest);
        Assert.False(viewModel.IsSubmitting);
        await harness.Navigator.DidNotReceiveWithAnyArgs().GoToAsync(default!, default!);
    }

    [Fact]
    public async Task AJobThatIsNoLongerAssignedReturnsToMyJobs()
    {
        var harness = Ready(out var viewModel, ProblemResponses.NotFound);
        viewModel.IsConfirmed = true;

        await viewModel.SubmitCommand.ExecuteAsync(null);

        await harness.Navigator.Received(1).GoToAsync(Routes.Jobs, Arg.Is<IDictionary<string, object>>(parameters =>
            (string)parameters[Routes.NoticeParameter] == Routes.JobUnassignedNotice));
    }

    [Fact]
    public void OfflineTheSubmitButtonKeepsItsPlaceDisabledWithTheOfflineHelper()
    {
        var harness = Ready(out var viewModel);

        harness.SetOnline(false);

        Assert.True(viewModel.ShowsOfflineBanner);
        Assert.False(viewModel.SubmitCommand.CanExecute(null));
        Assert.Equal("You're offline. Connect to submit the job.", viewModel.HelperText);
        harness.SetOnline(true);
        Assert.True(viewModel.SubmitCommand.CanExecute(null));
    }

    [Fact]
    public async Task WhileSubmittingTheButtonReadsSubmittingAndBackIsIgnored()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>();
        var harness = new JobsHarness(new StubHttpMessageHandler((_, _) => pending.Task));
        harness.Store.Remember(InspectionDtoBuilder.Complete1042(), JobDtoBuilder.Now);
        var viewModel = Open(harness, Routes.FromInspection);
        viewModel.IsConfirmed = true;

        var submit = viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("Submitting…", viewModel.SubmitLabel);
        Assert.True(viewModel.IsInputBlocked);
        Assert.True(viewModel.HandleBackButton());
        await viewModel.BackCommand.ExecuteAsync(null);
        await harness.Navigator.DidNotReceive().GoToAsync(Routes.Back);
        pending.SetResult(JobDtoBuilder.Json(JobDtoBuilder.Details1042()));
        await submit;
        Assert.Equal("Submit completed job", viewModel.SubmitLabel);
    }

    [Fact]
    public void FailedItemsListTheirReadingsWithLabelsAndUnits()
    {
        var inspection = InspectionDtoBuilder.WithAnswers(
            InspectionDtoBuilder.Complete1042(),
            InspectionDtoBuilder.Version,
            (3, InspectionDtoBuilder.Answer(InspectionResult.Fail, "Low charge", (InspectionDtoBuilder.Suction, 96.50m), (InspectionDtoBuilder.Discharge, 240m))));

        var items = ReviewAndSubmitViewModel.FailedItemsOf(inspection);

        Assert.Equal(["03 / 10", "05 / 10"], items.Select(item => item.NumberText));
        Assert.Equal(["Suction 96.5 psi", "Discharge 240 psi"], items[0].Readings);
        Assert.Equal("Item 03 / 10, Fail, Check refrigerant pressures, Low charge, Suction 96.5 psi, Discharge 240 psi", items[0].Description);
    }

    private static JobsHarness Ready(out ReviewAndSubmitViewModel viewModel, Func<HttpResponseMessage>? response = null, string source = Routes.FromInspection) =>
        Ready(out viewModel, source, response is null ? [] : [response]);

    private static JobsHarness Ready(out ReviewAndSubmitViewModel viewModel, Func<HttpResponseMessage> first, Func<HttpResponseMessage> second, string source = Routes.FromInspection) =>
        Ready(out viewModel, source, [first, second]);

    private static JobsHarness Ready(out ReviewAndSubmitViewModel viewModel, string source, Func<HttpResponseMessage>[] responses)
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(source == Routes.FromSheet
            ? [() => InspectionDtoBuilder.Json(InspectionDtoBuilder.Complete1042()), .. responses]
            : responses));
        harness.Store.Remember(InspectionDtoBuilder.Complete1042(), JobDtoBuilder.Now);
        viewModel = Open(harness, source);
        return harness;
    }

    private static ReviewAndSubmitViewModel Open(JobsHarness harness, string source)
    {
        harness.Store.RememberJob(new InspectionJob(JobDtoBuilder.Job1042, 1042, "Rooftop HVAC unit not cooling", "Rooftop HVAC service"));
        var viewModel = harness.CreateReview();
        viewModel.ApplyQueryAttributes(Query(source));
        return viewModel;
    }

    private static Dictionary<string, object> Query(string source) => new()
    {
        [Routes.JobIdParameter] = JobDtoBuilder.Job1042,
        [Routes.SourceParameter] = source,
    };

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
    }
}
