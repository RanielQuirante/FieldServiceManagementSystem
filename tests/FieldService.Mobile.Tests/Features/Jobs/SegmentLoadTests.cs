using System.Collections.Concurrent;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Screens;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Features.Jobs;

public sealed class SegmentLoadTests
{
    [Fact]
    public async Task BothSegmentsStayAvailableWhileTheChosenListLoads()
    {
        var (viewModel, _, completed, _) = await ActiveThenPendingCompletedAsync();

        Assert.True(viewModel.IsCompletedSelected);
        Assert.Equal(ScreenState.Loading, viewModel.State);
        Assert.True(viewModel.SelectViewCommand.CanExecute(AssignedJobsView.Active));
        Assert.True(viewModel.SelectViewCommand.CanExecute(AssignedJobsView.Completed));
        Assert.Equal("Completed, selected", viewModel.CompletedSegmentDescription);

        completed.SetResult(JobDtoBuilder.Json(JobDtoBuilder.MarcoCompleted()));
    }

    [Fact]
    public async Task ChoosingTheOtherSegmentDuringALoadShowsItAtOnceAndDropsTheLoadInFlight()
    {
        var (viewModel, harness, completed, tokens) = await ActiveThenPendingCompletedAsync();
        Assert.True(viewModel.SelectViewCommand.CanExecute(AssignedJobsView.Active));

        var switching = viewModel.SelectViewCommand.ExecuteAsync(AssignedJobsView.Active);

        Assert.True(viewModel.IsActiveSelected);
        Assert.Equal(ScreenState.Content, viewModel.State);
        Assert.Equal(["JOB-1042", "JOB-1047", "JOB-1053"], viewModel.Groups.SelectMany(group => group).Select(job => job.JobNumber));
        Assert.True(tokens["?view=completed"].IsCancellationRequested);
        completed.SetResult(JobDtoBuilder.Json(JobDtoBuilder.MarcoCompleted()));
        await switching;
        Assert.True(viewModel.IsActiveSelected);
        Assert.DoesNotContain(viewModel.Groups.SelectMany(group => group), job => job.JobNumber == "JOB-1036");
        Assert.Equal("?view=active", harness.Stub.Requests[^1].Query);
    }

    [Fact]
    public async Task ARepeatTapOnTheSelectedSegmentWhileItLoadsSendsNothing()
    {
        var (viewModel, harness, completed, _) = await ActiveThenPendingCompletedAsync();
        var requests = harness.Stub.Requests.Count;
        Assert.True(viewModel.SelectViewCommand.CanExecute(AssignedJobsView.Completed));

        await viewModel.SelectViewCommand.ExecuteAsync(AssignedJobsView.Completed);

        Assert.Equal(requests, harness.Stub.Requests.Count);
        Assert.True(viewModel.IsCompletedSelected);
        Assert.Equal(ScreenState.Loading, viewModel.State);
        completed.SetResult(JobDtoBuilder.Json(JobDtoBuilder.MarcoCompleted()));
    }

    [Fact]
    public async Task RefreshStaysAvailableWhileItsReloadRunsAndASecondPressSendsNothing()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var harness = new JobsHarness(new StubHttpMessageHandler((_, _) =>
            Interlocked.Increment(ref calls) == 1 ? Task.FromResult(JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())) : pending.Task));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);

        var refreshing = viewModel.RefreshCommand.ExecuteAsync(null);
        for (var attempt = 0; attempt < 200 && Volatile.Read(ref calls) < 2; attempt++)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        var available = viewModel.RefreshCommand.CanExecute(null);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        var requests = harness.Stub.Requests.Count;
        pending.SetResult(JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()));
        await refreshing;

        Assert.True(available);
        Assert.Equal(2, requests);
        Assert.Equal(2, Volatile.Read(ref calls));
        Assert.Equal(ScreenState.Content, viewModel.State);
        Assert.False(viewModel.IsRefreshing);
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
    }

    private static async Task<(MyJobsViewModel ViewModel, JobsHarness Harness, TaskCompletionSource<HttpResponseMessage> Completed, ConcurrentDictionary<string, CancellationToken> Tokens)> ActiveThenPendingCompletedAsync()
    {
        var completed = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokens = new ConcurrentDictionary<string, CancellationToken>(StringComparer.Ordinal);
        var harness = new JobsHarness(new StubHttpMessageHandler((request, token) =>
        {
            var query = request.RequestUri?.Query ?? string.Empty;
            tokens[query] = token;
            return query == "?view=completed"
                ? completed.Task
                : Task.FromResult(JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()));
        }));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        _ = viewModel.SelectViewCommand.ExecuteAsync(AssignedJobsView.Completed);
        for (var attempt = 0; attempt < 200 && !tokens.ContainsKey("?view=completed"); attempt++)
        {
            await Task.Delay(5);
        }

        Assert.True(tokens.ContainsKey("?view=completed"));
        return (viewModel, harness, completed, tokens);
    }
}
