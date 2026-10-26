using System.Collections.Concurrent;
using System.Net;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Shared.Screens;
using FieldService.Mobile.Tests.Shared.Testing;
using NSubstitute;

namespace FieldService.Mobile.Tests.Features.Jobs;

public sealed class LayoutSwitchTests
{
    private const string HoldNote = "Waiting for the replacement fan motor quote to be approved.";

    [Fact]
    public async Task CrossingNineHundredWithNoSheetOpenMovesKeyboardFocusToTheAvatarOfTheNewLayoutBothWays()
    {
        var (viewModel, harness, _) = await OpenTwoPaneAsync();
        var before = viewModel.AccountFocusRequest;

        Switch(harness, twoPane: false);
        Assert.False(viewModel.IsTwoPane);
        Assert.Equal(before + 1, viewModel.AccountFocusRequest);

        Switch(harness, twoPane: true);
        Assert.True(viewModel.IsTwoPane);
        Assert.Equal(before + 2, viewModel.AccountFocusRequest);
    }

    [Fact]
    public async Task CrossingNineHundredClosesTheAccountSheetAndAsksForFocusOnlyOnceForTheNewAvatar()
    {
        var (viewModel, harness, _) = await OpenTwoPaneAsync();
        viewModel.OpenAccountSheetCommand.Execute(null);
        var before = viewModel.AccountFocusRequest;

        Switch(harness, twoPane: false);

        Assert.False(viewModel.IsAccountSheetOpen);
        Assert.False(viewModel.HasOpenSheet);
        Assert.Equal(before + 1, viewModel.AccountFocusRequest);
    }

    [Fact]
    public async Task NarrowingWithThePanesStatusSheetOpenClosesItSendsNothingAndDropsTheDraft()
    {
        var (viewModel, harness, _) = await OpenTwoPaneAsync();
        var details = viewModel.Details;
        details.OpenStatusSheetCommand.Execute(null);
        details.SelectStatusOptionCommand.Execute(details.StatusOptions.First(option => option.RequiresNote));
        details.HoldReason = HoldNote;
        var requests = harness.Stub.Requests.Count;
        var triggerFocus = details.UpdateStatusFocusRequest;
        var avatarFocus = viewModel.AccountFocusRequest;

        Switch(harness, twoPane: false);

        Assert.False(details.IsStatusSheetOpen);
        Assert.False(viewModel.HasOpenSheet);
        Assert.Equal(string.Empty, details.HoldReason);
        Assert.Equal(requests, harness.Stub.Requests.Count);
        Assert.DoesNotContain(harness.Stub.Requests, request => request.Method == HttpMethod.Put);
        Assert.Equal(triggerFocus, details.UpdateStatusFocusRequest);
        Assert.Equal(avatarFocus + 1, viewModel.AccountFocusRequest);
        harness.ScreenReader.DidNotReceive().Announce(Arg.Any<string>());
    }

    [Fact]
    public async Task ABusyPaneSheetStaysThroughTheSwitchThenClosesAnnouncesTheChangeAndFocusesTheNewAvatar()
    {
        var (viewModel, harness, routes) = await OpenTwoPaneAsync();
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        routes.Puts.Enqueue(() => release.Task);
        routes.Gets.Enqueue(() => Task.FromResult(JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())));
        var details = viewModel.Details;
        details.OpenStatusSheetCommand.Execute(null);
        details.SelectStatusOptionCommand.Execute(details.StatusOptions.First(option => option.RequiresNote));
        details.HoldReason = HoldNote;
        var confirming = details.ConfirmStatusCommand.ExecuteAsync(null);
        var avatarFocus = viewModel.AccountFocusRequest;
        var triggerFocus = details.UpdateStatusFocusRequest;

        Switch(harness, twoPane: false);

        Assert.True(details.IsUpdatingStatus);
        Assert.True(details.IsStatusSheetOpen);
        Assert.True(viewModel.HasOpenSheet);
        Assert.Equal(avatarFocus, viewModel.AccountFocusRequest);

        release.SetResult(JobDtoBuilder.Json(OnHold1042()));
        await confirming;

        Assert.False(details.IsStatusSheetOpen);
        Assert.False(viewModel.HasOpenSheet);
        Assert.Equal(string.Empty, details.HoldReason);
        Assert.Equal(avatarFocus + 1, viewModel.AccountFocusRequest);
        Assert.Equal(triggerFocus, details.UpdateStatusFocusRequest);
        harness.ScreenReader.Received(1).Announce("JOB-1042 put on hold.");
        await WaitFor(() => harness.Stub.Requests.Count(request => request.Path == "/api/technicians/me/jobs") == 2);
    }

    [Fact]
    public async Task ABusyPaneSheetThatFailsAfterTheSwitchClosesWithoutAnyBannerAndFocusesTheNewAvatar()
    {
        var (viewModel, harness, routes) = await OpenTwoPaneAsync();
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        routes.Puts.Enqueue(() => release.Task);
        var details = viewModel.Details;
        details.OpenStatusSheetCommand.Execute(null);
        details.SelectStatusOptionCommand.Execute(details.StatusOptions.First(option => option.RequiresNote));
        details.HoldReason = HoldNote;
        var confirming = details.ConfirmStatusCommand.ExecuteAsync(null);
        var avatarFocus = viewModel.AccountFocusRequest;
        Switch(harness, twoPane: false);

        release.SetResult(ProblemResponses.ServerError(Guid.NewGuid().ToString("D")));
        await confirming;

        Assert.False(details.IsStatusSheetOpen);
        Assert.Null(details.SheetFailure);
        Assert.Null(details.ActionBanner);
        Assert.Equal(string.Empty, details.HoldReason);
        Assert.Equal(avatarFocus + 1, viewModel.AccountFocusRequest);
        harness.ScreenReader.DidNotReceive().Announce(Arg.Any<string>());
    }

    [Fact]
    public async Task TheSwitchLeavesTheAccountSheetOpenWhileSignOutRunsAndItClosesWhenSignOutEnds()
    {
        var (viewModel, harness, _) = await OpenTwoPaneAsync();
        var signOut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Session.SignOutAsync(Arg.Any<CancellationToken>()).Returns(signOut.Task);
        viewModel.OpenAccountSheetCommand.Execute(null);
        var signingOut = viewModel.SignOutCommand.ExecuteAsync(null);

        Switch(harness, twoPane: false);

        Assert.True(viewModel.IsAccountSheetOpen);
        Assert.True(viewModel.IsSigningOut);
        signOut.SetResult();
        await signingOut;
        Assert.False(viewModel.IsAccountSheetOpen);
    }

    [Fact]
    public async Task WhileSignOutRunsCancelTheOverlayBackAndEscDoNothingAndFocusStaysPut()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        var signOut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Session.SignOutAsync(Arg.Any<CancellationToken>()).Returns(signOut.Task);
        viewModel.OpenAccountSheetCommand.Execute(null);
        var focus = viewModel.AccountFocusRequest;
        var signingOut = viewModel.SignOutCommand.ExecuteAsync(null);

        viewModel.CloseAccountSheetCommand.Execute(null);
        Assert.True(viewModel.CloseSheet());
        BottomSheet.Close(viewModel.IsAccountSheetOpen, viewModel.CloseAccountSheetCommand);

        Assert.True(viewModel.IsAccountSheetOpen);
        Assert.True(viewModel.IsSigningOut);
        Assert.Equal(focus, viewModel.AccountFocusRequest);
        signOut.SetResult();
        await signingOut;
        Assert.False(viewModel.IsAccountSheetOpen);
        Assert.Equal(focus, viewModel.AccountFocusRequest);
    }

    [Fact]
    public async Task SignOutStaysAvailableWhileItRunsSoItsButtonKeepsFocusAndARepeatPressSendsNothing()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        var signOut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Session.SignOutAsync(Arg.Any<CancellationToken>()).Returns(signOut.Task);
        viewModel.OpenAccountSheetCommand.Execute(null);

        var first = viewModel.SignOutCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSigningOut);
        Assert.True(viewModel.SignOutCommand.CanExecute(null));
        await viewModel.SignOutCommand.ExecuteAsync(null);
        signOut.SetResult();
        await first;
        await harness.Session.Received(1).SignOutAsync(Arg.Any<CancellationToken>());
        Assert.False(viewModel.IsSigningOut);
    }

    [Fact]
    public async Task OnThePhoneJobDetailsTheSwitchClosesTheStatusSheetSendsNothingDropsTheDraftAndFocusesTheInPageBack()
    {
        var routes = new Routes();
        routes.Gets.Enqueue(() => Task.FromResult(JobDtoBuilder.Json(JobDtoBuilder.Details1042())));
        var harness = new JobsHarness(new StubHttpMessageHandler(routes.RespondAsync));
        var viewModel = harness.CreateDetails();
        viewModel.AppearingCommand.Execute(null);
        await viewModel.ShowJobAsync(JobDtoBuilder.Job1042);
        Switch(harness, twoPane: true);
        Assert.Equal(0, viewModel.StartFocusRequest);

        viewModel.OpenStatusSheetCommand.Execute(null);
        viewModel.SelectStatusOptionCommand.Execute(viewModel.StatusOptions.First(option => option.RequiresNote));
        viewModel.HoldReason = HoldNote;
        var triggerFocus = viewModel.UpdateStatusFocusRequest;
        Switch(harness, twoPane: false);

        Assert.False(viewModel.IsStatusSheetOpen);
        Assert.Equal(string.Empty, viewModel.HoldReason);
        Assert.Equal(1, viewModel.StartFocusRequest);
        Assert.Equal(triggerFocus, viewModel.UpdateStatusFocusRequest);
        Assert.DoesNotContain(harness.Stub.Requests, request => request.Method == HttpMethod.Put);

        viewModel.DisappearingCommand.Execute(null);
        viewModel.OpenStatusSheetCommand.Execute(null);
        Switch(harness, twoPane: true);
        Assert.True(viewModel.IsStatusSheetOpen);
        Assert.Equal(1, viewModel.StartFocusRequest);
    }

    private static async Task<(MyJobsViewModel ViewModel, JobsHarness Harness, Routes Routes)> OpenTwoPaneAsync()
    {
        var routes = new Routes();
        routes.Gets.Enqueue(() => Task.FromResult(JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())));
        routes.Gets.Enqueue(() => Task.FromResult(JobDtoBuilder.Json(JobDtoBuilder.Details1042())));
        var harness = new JobsHarness(new StubHttpMessageHandler(routes.RespondAsync), twoPane: true);
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        var job = viewModel.Groups[0][0];
        viewModel.SelectedJob = job;
        await viewModel.OpenJobCommand.ExecuteAsync(job);
        Assert.Equal(ScreenState.Content, viewModel.Details.State);
        return (viewModel, harness, routes);
    }

    private static void Switch(JobsHarness harness, bool twoPane)
    {
        harness.Layout.IsTwoPane.Returns(twoPane);
        harness.Layout.Changed += Raise.Event();
    }

    private static JobDetailsResponse OnHold1042()
    {
        var job = JobDtoBuilder.Details1042();
        var marco = new UserReference(JobDtoBuilder.Marco, "Marco Reyes");
        return job with
        {
            Status = JobStatus.OnHold,
            History = [.. job.History, JobDtoBuilder.History(JobHistoryAction.PutOnHold, JobStatus.InProgress, JobStatus.OnHold, marco, null, HoldNote, JobDtoBuilder.Local(10, 19, 10, 31))],
            Version = 12,
            Affordances = new JobAffordances([new AllowedStatusChange(JobStatus.InProgress, false)], false),
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

    private sealed class Routes
    {
        public ConcurrentQueue<Func<Task<HttpResponseMessage>>> Gets { get; } = new();

        public ConcurrentQueue<Func<Task<HttpResponseMessage>>> Puts { get; } = new();

        public Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var queue = request.Method == HttpMethod.Put ? Puts : Gets;
            return queue.TryDequeue(out var next)
                ? next()
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
