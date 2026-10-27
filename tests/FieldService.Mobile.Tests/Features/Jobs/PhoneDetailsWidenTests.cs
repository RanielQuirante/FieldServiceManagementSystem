using System.Collections.Concurrent;
using System.Net;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Shared.Navigation;
using FieldService.Mobile.Shared.Screens;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui.Controls;
using NSubstitute;

namespace FieldService.Mobile.Tests.Features.Jobs;

public sealed class PhoneDetailsWidenTests
{
    private const string HoldNote = "Waiting for the replacement fan motor quote to be approved.";

    [Fact]
    public async Task WideningPastNineHundredTakesThePhoneJobDetailsOffWithoutAnimationAndHandsItsLoadedJobToThePanes()
    {
        var (details, harness, _) = await PhoneDetailsAsync();

        Switch(harness, twoPane: true);

        var handoff = HandedOff(harness);
        Assert.Equal(JobDtoBuilder.Job1042, handoff.JobId);
        Assert.Equal(JobDtoBuilder.Job1042, handoff.Job?.Id);
        Assert.NotNull(handoff.LoadedAt);
        Assert.Equal(0, details.StartFocusRequest);
        harness.ScreenReader.DidNotReceive().Announce(Arg.Any<string>());
    }

    [Fact]
    public async Task WideningWithTheStatusSheetAndAHoldDraftClosesItSendsNothingDropsTheDraftAndStillMoves()
    {
        var (details, harness, _) = await PhoneDetailsAsync();
        details.OpenStatusSheetCommand.Execute(null);
        details.SelectStatusOptionCommand.Execute(details.StatusOptions.First(option => option.RequiresNote));
        details.HoldReason = HoldNote;

        Switch(harness, twoPane: true);

        Assert.False(details.IsStatusSheetOpen);
        Assert.Equal(string.Empty, details.HoldReason);
        Assert.DoesNotContain(harness.Stub.Requests, request => request.Method == HttpMethod.Put);
        Assert.Equal(JobDtoBuilder.Job1042, HandedOff(harness).JobId);
        Assert.Equal(0, details.StartFocusRequest);
    }

    [Fact]
    public async Task WideningWhileAHoldIsBeingSentKeepsTheSheetUntilTheAnswerThenAnnouncesItAndMoves()
    {
        var (details, harness, responses) = await PhoneDetailsAsync();
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        responses.Puts.Enqueue(() => release.Task);
        var confirming = await StartHoldAsync(details);

        Switch(harness, twoPane: true);

        Assert.True(details.IsStatusSheetOpen);
        await harness.Navigator.DidNotReceive().GoToAsync(Arg.Any<string>(), Arg.Any<IDictionary<string, object>>(), Arg.Any<bool>());
        release.SetResult(JobDtoBuilder.Json(OnHold1042()));
        await confirming;

        Assert.False(details.IsStatusSheetOpen);
        harness.ScreenReader.Received(1).Announce(Arg.Is<string>(text => text.Contains("put on hold", StringComparison.Ordinal)));
        Assert.Equal(JobStatus.OnHold, HandedOff(harness).Job?.Status);
        Assert.Equal(0, details.StartFocusRequest);
    }

    [Fact]
    public async Task AHoldThatFailsAfterTheWideningShowsNoBannerAndStillMoves()
    {
        var (details, harness, responses) = await PhoneDetailsAsync();
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        responses.Puts.Enqueue(() => release.Task);
        var confirming = await StartHoldAsync(details);

        Switch(harness, twoPane: true);
        release.SetResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        await confirming;

        Assert.False(details.IsStatusSheetOpen);
        Assert.Null(details.SheetFailure);
        Assert.Null(details.ActionBanner);
        Assert.Equal(JobDtoBuilder.Job1042, HandedOff(harness).JobId);
    }

    [Fact]
    public async Task NarrowedAgainBeforeTheAnswerTheBusyHoldStaysOnThePhonePageAndFocusGoesToItsBack()
    {
        var (details, harness, responses) = await PhoneDetailsAsync();
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        responses.Puts.Enqueue(() => release.Task);
        var confirming = await StartHoldAsync(details);

        Switch(harness, twoPane: true);
        Switch(harness, twoPane: false);
        release.SetResult(JobDtoBuilder.Json(OnHold1042()));
        await confirming;

        await harness.Navigator.DidNotReceive().GoToAsync(Arg.Any<string>(), Arg.Any<IDictionary<string, object>>(), Arg.Any<bool>());
        Assert.False(details.IsStatusSheetOpen);
        Assert.Equal(1, details.StartFocusRequest);
    }

    [Fact]
    public async Task ThePanesShowTheHandedJobAtOnceSelectItsCardScrollItIntoViewAndFocusTheAvatar()
    {
        var (jobs, harness) = await TwoPaneJobsAsync();
        var avatar = jobs.AccountFocusRequest;

        jobs.ApplyQueryAttributes(Handed(JobDtoBuilder.Details1042()));

        Assert.Equal(JobDtoBuilder.Job1042, jobs.Details.JobId);
        Assert.NotNull(jobs.Details.Job);
        Assert.Equal(ScreenState.Content, jobs.Details.State);
        Assert.Equal("JOB-1042", jobs.SelectedJob?.JobNumber);
        Assert.False(jobs.KeepsPaneJob);
        Assert.True(jobs.ShowsPaneJob);
        Assert.Equal(1, jobs.SelectedIntoViewRequest);
        Assert.Equal(avatar + 1, jobs.AccountFocusRequest);
        Assert.Equal(AssignedJobsView.Active, jobs.SelectedView);
        harness.ScreenReader.DidNotReceive().Announce(Arg.Any<string>());
    }

    [Fact]
    public async Task AHandedJobThatIsNotInTheChosenSegmentShowsInThePaneWithNoCardSelected()
    {
        var (jobs, _) = await TwoPaneJobsAsync(completed: true);

        jobs.ApplyQueryAttributes(Handed(JobDtoBuilder.Details1042()));

        Assert.Equal(AssignedJobsView.Completed, jobs.SelectedView);
        Assert.Null(jobs.SelectedJob);
        Assert.True(jobs.KeepsPaneJob);
        Assert.True(jobs.ShowsPaneJob);
        Assert.Equal(0, jobs.SelectedIntoViewRequest);
        Assert.Equal(JobDtoBuilder.Job1042, jobs.Details.JobId);
    }

    [Fact]
    public async Task OfflineThePanesShowTheHandedJobAsOfItsLoadTimeAndNeverTheLoadFailure()
    {
        var (jobs, harness) = await TwoPaneJobsAsync();
        harness.SetOnline(false);

        jobs.ApplyQueryAttributes(Handed(JobDtoBuilder.Details1042()));

        Assert.Equal(ScreenState.Offline, jobs.Details.State);
        Assert.NotNull(jobs.Details.Job);
        Assert.Null(jobs.Details.Error);
        Assert.StartsWith("Showing this job as of ", jobs.Details.OfflineMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHandedJobThatLeftTheSegmentSinceTheListLoadedStaysInThePaneWhenTheReloadDropsItsCard()
    {
        var (jobs, harness) = await ReturningJobsAsync(new GetAssignedJobsResponse([JobDtoBuilder.Job1047Summary(), JobDtoBuilder.Job1053Summary()]));
        var watch = new GroupedListWatch(jobs.Groups)
        {
            ContainerLost = job =>
            {
                if (ReferenceEquals(jobs.SelectedJob, job))
                {
                    jobs.SelectedJob = null;
                }
            },
        };
        var pane = new List<bool>();
        jobs.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MyJobsViewModel.ShowsPaneJob))
            {
                pane.Add(jobs.ShowsPaneJob);
            }
        };

        jobs.ApplyQueryAttributes(Handed(JobDtoBuilder.Details1042() with { Status = JobStatus.Cancelled }));
        await jobs.AppearingCommand.ExecuteAsync(null);
        await Settled(harness, jobs);

        Assert.DoesNotContain(jobs.Groups.SelectMany(group => group), job => job.Id == JobDtoBuilder.Job1042);
        Assert.Null(jobs.SelectedJob);
        Assert.True(jobs.KeepsPaneJob);
        Assert.True(jobs.ShowsPaneJob);
        Assert.False(jobs.ShowsNoSelection);
        Assert.DoesNotContain(false, pane);
        Assert.Equal(JobDtoBuilder.Job1042, jobs.Details.JobId);
        Assert.Empty(watch.Inconsistencies);
    }

    [Fact]
    public async Task AHandedJobWhoseStatusChangedOnThePhoneKeepsItsCardSelectedAndAsksAgainToBringItIntoViewAfterTheReload()
    {
        var (jobs, harness) = await ReturningJobsAsync(new GetAssignedJobsResponse([JobDtoBuilder.Job1042Summary() with { Status = JobStatus.OnHold }, JobDtoBuilder.Job1047Summary(), JobDtoBuilder.Job1053Summary()]));
        var card = jobs.Groups[0][0];
        var watch = new GroupedListWatch(jobs.Groups)
        {
            ContainerLost = job =>
            {
                if (ReferenceEquals(jobs.SelectedJob, job))
                {
                    jobs.SelectedJob = null;
                }
            },
        };

        jobs.ApplyQueryAttributes(Handed(JobDtoBuilder.Details1042() with { Status = JobStatus.OnHold }));
        await jobs.AppearingCommand.ExecuteAsync(null);
        await Settled(harness, jobs);

        Assert.Same(card, jobs.SelectedJob);
        Assert.Equal(JobStatus.OnHold, card.Status);
        Assert.Equal(2, jobs.SelectedIntoViewRequest);
        Assert.Empty(watch.Actions);
        Assert.False(jobs.KeepsPaneJob);
    }

    [Fact]
    public async Task AHandedJobReachingAPhoneMyJobsIsDropped()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())));
        var jobs = harness.CreateMyJobs();
        await jobs.AppearingCommand.ExecuteAsync(null);

        jobs.ApplyQueryAttributes(Handed(JobDtoBuilder.Details1042()));

        Assert.Null(jobs.SelectedJob);
        Assert.False(jobs.KeepsPaneJob);
        Assert.Equal(0, jobs.SelectedIntoViewRequest);
    }

    [Fact]
    public void BackFromAPageAboveAPhoneJobDetailsInTwoPanesSkipsThatJobDetailsAndOnlyIt()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence());
        var phone = new ContentPage { BindingContext = harness.CreateDetails() };
        var pane = harness.CreateDetails();
        pane.IsInPane = true;
        var paneDetails = new ContentPage { BindingContext = pane };
        var top = new ContentPage();
        ((JobDetailsViewModel)phone.BindingContext).ApplyQueryAttributes(JobsHarness.JobQuery(JobDtoBuilder.Job1042));

        Assert.Same(phone, ShellNavigator.PhoneOnlyPageBelowTop([null, phone, top], twoPane: true, Routes.Back));
        Assert.Null(ShellNavigator.PhoneOnlyPageBelowTop([null, phone, top], twoPane: false, Routes.Back));
        Assert.Null(ShellNavigator.PhoneOnlyPageBelowTop([null, phone, top], twoPane: true, Routes.BackTwo));
        Assert.Null(ShellNavigator.PhoneOnlyPageBelowTop([null, phone], twoPane: true, Routes.Back));
        Assert.Null(ShellNavigator.PhoneOnlyPageBelowTop([null, paneDetails, top], twoPane: true, Routes.Back));
        Assert.Null(ShellNavigator.PhoneOnlyPageBelowTop([null, new ContentPage(), top], twoPane: true, Routes.Back));
    }

    [Fact]
    public void TheCardScrolledIntoViewIsLookedUpInItsDayGroup()
    {
        var first = new List<object> { "a", "b" };
        var second = new List<object> { "c" };

        Assert.Same(second, ListFocus.GroupOf(new[] { first, second }, "c"));
        Assert.Same(first, ListFocus.GroupOf(new[] { first, second }, "a"));
        Assert.Null(ListFocus.GroupOf(new[] { first, second }, "z"));
        Assert.Null(ListFocus.GroupOf(null, "a"));
    }

    [Fact]
    public void TheTwoPaneListBindsTheScrollRequestOfTheSelectedCard()
    {
        var page = System.Xml.Linq.XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Features", "Jobs", "MyJobsPage.xaml"));
        System.Xml.Linq.XNamespace controls = "clr-namespace:FieldService.Mobile.Shared.Controls";

        Assert.Single(page.Descendants(), element => element.Name.LocalName == "CollectionView" && element.Attribute(controls + "ListFocus.SelectedIntoViewRequest")?.Value == "{Binding SelectedIntoViewRequest}");
    }

    private static PaneJobHandoff HandedOff(JobsHarness harness)
    {
        var call = harness.Navigator.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(INavigator.GoToAsync) && call.GetArguments().Length == 3);
        var arguments = call.GetArguments();
        Assert.Equal(Routes.Back, arguments[0]);
        Assert.False((bool)arguments[2]!);
        return Assert.IsType<PaneJobHandoff>(((IDictionary<string, object>)arguments[1]!)[Routes.PaneJobParameter]);
    }

    private static Dictionary<string, object> Handed(JobDetailsResponse job) =>
        new() { [Routes.PaneJobParameter] = new PaneJobHandoff(job.Id, job, JobDtoBuilder.Now.AddMinutes(-5)) };

    private static async Task<(JobDetailsViewModel Details, JobsHarness Harness, Responses Responses)> PhoneDetailsAsync()
    {
        var responses = new Responses();
        responses.Gets.Enqueue(() => Task.FromResult(JobDtoBuilder.Json(JobDtoBuilder.Details1042())));
        var harness = new JobsHarness(new StubHttpMessageHandler(responses.RespondAsync));
        var details = harness.CreateDetails();
        details.AppearingCommand.Execute(null);
        await details.ShowJobAsync(JobDtoBuilder.Job1042);
        harness.Navigator.ClearReceivedCalls();
        return (details, harness, responses);
    }

    private static async Task<Task> StartHoldAsync(JobDetailsViewModel details)
    {
        details.OpenStatusSheetCommand.Execute(null);
        details.SelectStatusOptionCommand.Execute(details.StatusOptions.First(option => option.RequiresNote));
        details.HoldReason = HoldNote;
        var confirming = details.ConfirmStatusCommand.ExecuteAsync(null);
        for (var attempt = 0; attempt < 200 && !details.IsUpdatingStatus; attempt++)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        Assert.True(details.IsUpdatingStatus);
        return confirming;
    }

    private static async Task<(MyJobsViewModel Jobs, JobsHarness Harness)> TwoPaneJobsAsync(bool completed = false)
    {
        var responses = new Responses();
        responses.Gets.Enqueue(() => Task.FromResult(JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())));
        if (completed)
        {
            responses.Gets.Enqueue(() => Task.FromResult(JobDtoBuilder.Json(JobDtoBuilder.MarcoCompleted())));
        }

        responses.Gets.Enqueue(() => Task.FromResult(JobDtoBuilder.Json(JobDtoBuilder.Details1042())));
        var harness = new JobsHarness(new StubHttpMessageHandler(responses.RespondAsync), twoPane: true);
        var jobs = harness.CreateMyJobs();
        await jobs.AppearingCommand.ExecuteAsync(null);
        if (completed)
        {
            await jobs.SelectViewCommand.ExecuteAsync(AssignedJobsView.Completed);
        }

        return (jobs, harness);
    }

    private static async Task<(MyJobsViewModel Jobs, JobsHarness Harness)> ReturningJobsAsync(GetAssignedJobsResponse reloaded)
    {
        var lists = new ConcurrentQueue<GetAssignedJobsResponse>([JobDtoBuilder.MarcoActive(), reloaded]);
        var harness = new JobsHarness(
            new StubHttpMessageHandler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/me/jobs", StringComparison.Ordinal)
                ? lists.TryDequeue(out var list) ? JobDtoBuilder.Json(list) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : JobDtoBuilder.Json(JobDtoBuilder.Details1042()))),
            twoPane: true);
        var jobs = harness.CreateMyJobs();
        await jobs.AppearingCommand.ExecuteAsync(null);
        jobs.DisappearingCommand.Execute(null);
        return (jobs, harness);
    }

    private static async Task Settled(JobsHarness harness, MyJobsViewModel jobs)
    {
        for (var attempt = 0; attempt < 400 && (harness.Stub.Requests.Count < 3 || jobs.IsBusy || jobs.Details.State == ScreenState.Loading); attempt++)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
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

    private sealed class Responses
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
