using System.Net;
using System.Xml.Linq;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Shared.Screens;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class BannerLeaveTests
{
    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";

    public static TheoryData<string?, string?, string?, string?, string?, string?, bool> AnnouncementCases => new()
    {
        { null, null, null, "You're offline.", "Showing jobs from 10:31 AM.", null, true },
        { "You're offline.", "Showing jobs from 10:31 AM.", null, "You're offline.", null, null, false },
        { "Jobs could not be refreshed.", "Showing jobs from 6:50 PM.", "abc", null, "Showing jobs from 6:50 PM.", "abc", false },
        { "Jobs could not be refreshed.", "Showing jobs from 6:50 PM.", "abc", null, null, null, false },
        { "Jobs could not be refreshed.", "Showing jobs from 6:50 PM.", null, "Jobs could not be refreshed.", "Try again in 12 seconds.", null, true },
        { "Jobs could not be refreshed.", "Try again in 12 seconds.", "abc", "Jobs could not be refreshed.", "Showing jobs from 6:50 PM.", null, true },
        { "You're offline.", "Showing jobs from 10:31 AM.", null, "You're offline.", "Showing jobs from 10:31 AM.", null, false },
    };

    [Theory]
    [MemberData(nameof(AnnouncementCases))]
    public void ABannerIsAnnouncedWhenItAppearsOrANewMessageReplacesItNeverWhileItClears(
        string? title,
        string? message,
        string? reference,
        string? nextTitle,
        string? nextMessage,
        string? nextReference,
        bool announces)
    {
        var announced = title is null && message is null && reference is null ? null : new BannerText(title, message, reference);

        Assert.Equal(announces, Banner.ShouldAnnounce(announced, new BannerText(nextTitle, nextMessage, nextReference)));
    }

    [Fact]
    public void ABannerWithNoTextAnnouncesNothing() =>
        Assert.False(Banner.ShouldAnnounce(null, new BannerText(null, " ", null)));

    [Fact]
    public async Task OnMyJobsTheOfflineBannerLeavingWithFocusMovesFocusToTheFirstDayGroupOnceTheListIsBack()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()),
            () => JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        harness.SetOnline(false);
        Assert.Equal(ScreenState.Offline, viewModel.State);

        harness.SetOnline(true);
        await WaitFor(() => viewModel.State == ScreenState.Content);
        viewModel.BannerLeftWithFocusCommand.Execute(null);

        Assert.Equal(1, viewModel.GroupFocusRequest);
        Assert.Equal(0, viewModel.EmptyFocusRequest);
        Assert.Equal(0, viewModel.TitleFocusRequest);
    }

    [Fact]
    public async Task OnMyJobsAnErrorBannerThatLeavesWhileTheListLoadsMovesFocusOnlyWhenTheListArrives()
    {
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var harness = new JobsHarness(new StubHttpMessageHandler((_, _) => ++calls == 1
            ? Task.FromResult(ProblemResponses.ServerError(Guid.NewGuid().ToString("D")))
            : release.Task));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);
        Assert.Equal(ScreenState.Error, viewModel.State);

        var retrying = viewModel.RetryCommand.ExecuteAsync(null);
        Assert.Equal(ScreenState.Loading, viewModel.State);
        viewModel.BannerLeftWithFocusCommand.Execute(null);
        Assert.Equal(0, viewModel.GroupFocusRequest);

        release.SetResult(JobDtoBuilder.Json(new GetAssignedJobsResponse([])));
        await retrying;

        Assert.Equal(ScreenState.Empty, viewModel.State);
        Assert.Equal(1, viewModel.EmptyFocusRequest);
        Assert.Equal(0, viewModel.GroupFocusRequest);
    }

    [Fact]
    public async Task APendingContentFocusIsDroppedWhenTheReloadFailsAgain()
    {
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var harness = new JobsHarness(new StubHttpMessageHandler((_, _) =>
        {
            var call = Interlocked.Increment(ref calls);
            return call switch
            {
                2 => release.Task,
                3 => Task.FromResult(JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())),
                _ => Task.FromResult(ProblemResponses.ServerError(Guid.NewGuid().ToString("D"))),
            };
        }));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);

        var retrying = viewModel.RetryCommand.ExecuteAsync(null);
        Assert.Equal(ScreenState.Loading, viewModel.State);
        viewModel.BannerLeftWithFocusCommand.Execute(null);
        release.SetResult(ProblemResponses.ServerError(Guid.NewGuid().ToString("D")));
        await retrying;
        Assert.Equal(ScreenState.Error, viewModel.State);
        await viewModel.RetryCommand.ExecuteAsync(null);

        Assert.Equal(ScreenState.Content, viewModel.State);
        Assert.Equal(0, viewModel.GroupFocusRequest);
    }

    [Fact]
    public async Task TryAgainStaysAvailableWhileItRunsAndARepeatPressSendsNoSecondRequest()
    {
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var harness = new JobsHarness(new StubHttpMessageHandler((_, _) => ++calls == 1
            ? Task.FromResult(ProblemResponses.ServerError(Guid.NewGuid().ToString("D")))
            : release.Task));
        var viewModel = harness.CreateMyJobs();
        await viewModel.AppearingCommand.ExecuteAsync(null);

        var first = viewModel.RetryCommand.ExecuteAsync(null);
        Assert.True(viewModel.RetryCommand.CanExecute(null));
        await viewModel.RetryCommand.ExecuteAsync(null);
        Assert.Equal(2, harness.Stub.Requests.Count);
        release.SetResult(JobDtoBuilder.Json(JobDtoBuilder.MarcoActive()));
        await first;
        Assert.Equal(ScreenState.Content, viewModel.State);
    }

    [Fact]
    public async Task OnJobDetailsABannerLeavingWithFocusMovesFocusToTheJobTitle()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            () => JobDtoBuilder.Json(JobDtoBuilder.Details1042())));
        var viewModel = harness.CreateDetails();
        viewModel.AppearingCommand.Execute(null);
        await viewModel.ShowJobAsync(JobDtoBuilder.Job1042);
        Assert.Equal(ScreenState.Error, viewModel.State);
        var before = viewModel.TitleFocusRequest;

        await viewModel.RetryCommand.ExecuteAsync(null);
        viewModel.BannerLeftWithFocusCommand.Execute(null);

        Assert.Equal(ScreenState.Content, viewModel.State);
        Assert.Equal(before + 1, viewModel.TitleFocusRequest);
    }

    [Theory]
    [InlineData("Features/Jobs/MyJobsPage.xaml", 6)]
    [InlineData("Features/Jobs/JobDetailsView.xaml", 3)]
    [InlineData("Features/Inspection/InspectionPage.xaml", 2)]
    [InlineData("Features/Inspection/ReviewAndSubmitPage.xaml", 2)]
    public void EveryBannerWithTryAgainReportsALeaveWithFocusInside(string page, int count)
    {
        var banners = Load(page).Descendants(_controls + "Banner").Where(banner => banner.Attribute("ActionText") is not null).ToList();

        Assert.Equal(count, banners.Count);
        Assert.All(banners, banner => Assert.Equal("{Binding BannerLeftWithFocusCommand}", banner.Attribute("LeaveFocusCommand")?.Value));
    }

    [Fact]
    public void MyJobsBindsEachContentStartToItsFocusRequestInBothLayouts()
    {
        var page = Load("Features/Jobs/MyJobsPage.xaml");

        Assert.Equal(2, page.Descendants(_maui + "CollectionView").Count(list => list.Attribute(_controls + "ListFocus.FirstGroupRequest")?.Value == "{Binding GroupFocusRequest}"));
        Assert.Equal(2, page.Descendants(_controls + "EmptyState").Count(empty => empty.Attribute("TitleFocusRequest")?.Value == "{Binding EmptyFocusRequest}"));
        Assert.Contains(page.Descendants(_controls + "AppHeader"), header => header.Attribute("TitleFocusRequest")?.Value == "{Binding TitleFocusRequest}");
        Assert.Contains(page.Descendants(_maui + "Label"), label => label.Attribute(_controls + "ScrollRequests.SemanticFocus")?.Value == "{Binding TitleFocusRequest}");
        foreach (var pushed in new[] { "Features/Inspection/InspectionPage.xaml", "Features/Inspection/ReviewAndSubmitPage.xaml" })
        {
            Assert.Equal("{Binding TitleFocusRequest}", Load(pushed).Descendants(_controls + "AppHeader").Single().Attribute("TitleFocusRequest")?.Value);
        }
    }

    [Fact]
    public void TheBannerReportsALeaveOnlyWhenFocusWasInsideItAndNeverAnnouncesFromTheTextChangeItself()
    {
        var banner = Source("Shared/Controls/Banner.xaml.cs");

        Assert.Contains("FocusMoves.HasFocusWithin(this) == true", banner, StringComparison.Ordinal);
        Assert.Contains("protected override void OnPropertyChanging(", banner, StringComparison.Ordinal);
        Assert.Contains("ShouldAnnounce(_announced, text)", banner, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.Dispatch(", banner, StringComparison.Ordinal);
        Assert.DoesNotContain("_announced = null;\n        Announce();", banner.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("FocusMoves.FocusWithinCommand", Source("Platforms/Android/AndroidPlatformSetup.cs"), StringComparison.Ordinal);
        Assert.Contains("AccessibilityFocused", Source("Platforms/Android/AndroidPlatformSetup.cs"), StringComparison.Ordinal);
        Assert.Contains("FocusMoves.FocusWithinCommand", Source("Platforms/Windows/WindowsPlatformSetup.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheFirstDayGroupHeadingIsTheLabelBoundToTheFirstGroupWithAHeadingLevel()
    {
        var first = new object();
        var second = new object();
        var plain = new Label { BindingContext = first };
        var heading = new Label { BindingContext = first };
        SemanticProperties.SetHeadingLevel(heading, SemanticHeadingLevel.Level2);
        var other = new Label { BindingContext = second };
        SemanticProperties.SetHeadingLevel(other, SemanticHeadingLevel.Level2);

        Assert.Same(first, ListFocus.FirstGroup(new[] { first, second }));
        Assert.Null(ListFocus.FirstGroup(Array.Empty<object>()));
        Assert.Same(heading, ListFocus.GroupHeading([other, plain, heading], first));
        Assert.Null(ListFocus.GroupHeading([other, plain], first));
    }

    private static XDocument Load(string relative) =>
        XDocument.Load(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(5);
        }

        Assert.True(condition());
    }
}
