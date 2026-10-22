using System.Xml.Linq;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Navigation;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;

namespace FieldService.Mobile.Tests.Shared.Navigation;

public sealed class ShellNavigationTests : IDisposable
{
    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";

    private readonly Shell _shell;
    private readonly ShellBackedNavigator _navigator;

    public ShellNavigationTests()
    {
        DispatcherProvider.SetCurrent(new InlineDispatcherProvider());
        foreach (var route in AppShell.PushedRoutes.Keys)
        {
            Routing.RegisterRoute(route, typeof(ProbePage));
        }

        _shell = new Shell();
        foreach (var route in ShellContentRoutes())
        {
            _shell.Items.Add(new ShellContent { Route = route, ContentTemplate = new DataTemplate(() => new ProbePage()) });
        }

        _ = new Window(_shell);
        _navigator = new ShellBackedNavigator(_shell);
    }

    public void Dispose()
    {
        foreach (var route in AppShell.PushedRoutes.Keys)
        {
            Routing.UnRegisterRoute(route);
        }
    }

    [Fact]
    public void TheShellDeclaresSignInAndMyJobsAndPushesJobDetails()
    {
        Assert.Equal([Routes.SignInRoute, Routes.JobsRoute], ShellContentRoutes());
        var pushed = Assert.Single(AppShell.PushedRoutes);
        Assert.Equal(Routes.JobDetailsRoute, pushed.Key);
        Assert.Equal(typeof(JobDetailsPage), pushed.Value);
    }

    [Fact]
    public async Task TappingAJobInMyJobsOpensJobDetailsWithTheJobId()
    {
        await _navigator.GoToAsync(Routes.Jobs);
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())), navigator: _navigator);
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        var job = viewModel.Groups[0][1];

        await viewModel.OpenJobCommand.ExecuteAsync(job);

        Assert.Equal("//" + Routes.JobDetailsRoute, _shell.CurrentState.Location.OriginalString);
        var page = Assert.IsType<ProbePage>(_shell.Navigation.NavigationStack[^1]);
        Assert.Equal(JobDtoBuilder.Job1047, page.Query[Routes.JobIdParameter]);
        Assert.DoesNotContain("jobs.open-failed", harness.Logs.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AJobThatIsNoLongerAssignedReturnsToMyJobsWithTheNotice()
    {
        await _navigator.GoToAsync(Routes.Jobs);
        await _navigator.GoToAsync(Routes.JobDetails, new Dictionary<string, object> { [Routes.JobIdParameter] = JobDtoBuilder.Job1047 });
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(ProblemResponses.NotFound), navigator: _navigator);
        var viewModel = harness.CreateDetails();

        await viewModel.ShowJobAsync(JobDtoBuilder.Job1047);

        Assert.Equal("//" + Routes.JobsRoute, _shell.CurrentState.Location.OriginalString);
        Assert.Single(_shell.Navigation.NavigationStack);
        Assert.Equal(Routes.JobUnassignedNotice, _navigator.LastParameters?[Routes.NoticeParameter]);
        Assert.DoesNotContain("jobs.navigate-failed", harness.Logs.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BackFromJobDetailsReturnsToMyJobs()
    {
        await _navigator.GoToAsync(Routes.Jobs);
        await _navigator.GoToAsync(Routes.JobDetails, new Dictionary<string, object> { [Routes.JobIdParameter] = JobDtoBuilder.Job1047 });
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(), navigator: _navigator);

        await harness.CreateDetails().BackCommand.ExecuteAsync(null);

        Assert.Equal("//" + Routes.JobsRoute, _shell.CurrentState.Location.OriginalString);
        Assert.DoesNotContain("jobs.navigate-failed", harness.Logs.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSessionAndStartupRoutesSwitchBetweenSignInAndMyJobs()
    {
        await _navigator.GoToAsync(Routes.Jobs);
        Assert.Equal("//" + Routes.JobsRoute, _shell.CurrentState.Location.OriginalString);

        await _navigator.GoToAsync(Routes.JobDetails, new Dictionary<string, object> { [Routes.JobIdParameter] = JobDtoBuilder.Job1047 });
        await _navigator.GoToAsync(Routes.SignIn);
        Assert.Equal("//" + Routes.SignInRoute, _shell.CurrentState.Location.OriginalString);

        await _navigator.GoToAsync(Routes.Jobs);
        Assert.Equal("//" + Routes.JobsRoute, _shell.CurrentState.Location.OriginalString);
    }

    [Fact]
    public async Task ShellRejectsARelativeRouteThatStartsWithItsOwnContentRoute()
    {
        await _navigator.GoToAsync(Routes.Jobs);

        var failure = await Assert.ThrowsAsync<Exception>(() => _shell.GoToAsync(Routes.JobDetailsRoute));

        Assert.Contains("Relative routing to shell elements is currently not supported", failure.Message, StringComparison.Ordinal);
        Assert.StartsWith("//", Routes.JobDetails, StringComparison.Ordinal);
    }

    private static List<string> ShellContentRoutes() =>
        [.. XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "AppShell.xaml"))
            .Descendants(_maui + "ShellContent")
            .Select(content => content.Attribute("Route")!.Value)];

    private sealed class ShellBackedNavigator(Shell shell) : INavigator
    {
        public IDictionary<string, object>? LastParameters { get; private set; }

        public Task GoToAsync(string route) => shell.GoToAsync(route);

        public Task GoToAsync(string route, IDictionary<string, object> parameters)
        {
            LastParameters = parameters;
            return shell.GoToAsync(route, new ShellNavigationQueryParameters(parameters));
        }
    }

    private sealed class ProbePage : ContentPage, IQueryAttributable
    {
        public Dictionary<string, object> Query { get; } = [];

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            foreach (var (key, value) in query)
            {
                Query[key] = value;
            }
        }
    }

    private sealed class InlineDispatcherProvider : IDispatcherProvider
    {
        public IDispatcher? GetForCurrentThread() => new InlineDispatcher();
    }

    private sealed class InlineDispatcher : IDispatcher
    {
        public bool IsDispatchRequired => false;

        public bool Dispatch(Action action)
        {
            action();
            return true;
        }

        public bool DispatchDelayed(TimeSpan delay, Action action)
        {
            action();
            return true;
        }

        public IDispatcherTimer CreateTimer() => throw new NotSupportedException();
    }
}
