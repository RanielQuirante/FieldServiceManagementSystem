using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Formatting;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace FieldService.Mobile.Tests.Features.Jobs;

public sealed class AssignedJobActionsTests
{
    [Fact]
    public async Task AnAssignedJobShowsTheAssignedLayoutWithTheScheduleInTheHero()
    {
        var (viewModel, _) = await Load(JobDtoBuilder.Details1047());

        Assert.True(viewModel.IsAssigned);
        Assert.True(viewModel.ShowsDock);
        Assert.False(viewModel.ShowsHeroActions);
        Assert.Equal("Today 1:30–" + DisplayFormats.ZeroWidthSpace + "3:00 PM", viewModel.ScheduleLine);
        Assert.Equal(Spaces.Text("Starts in 2_h_48_min"), viewModel.StartsLine);
        Assert.Equal("Assigned to you by Dana Whitfield at 10:05 AM", viewModel.AssignedLine);
        Assert.False(viewModel.ShowsScheduledRow);
        Assert.False(viewModel.ShowsRowActions);
        Assert.Equal("(555) 014-4417", viewModel.ContactValue);
        Assert.Null(viewModel.ContactSecondary);
        Assert.Equal("Rooftop HVAC service", viewModel.ChecklistName);
        Assert.Equal(2, viewModel.Timeline.Count);
        Assert.True(viewModel.Timeline[1].IsNewest);
        Assert.Equal("Assigned to Marco Reyes", viewModel.Timeline[1].Text);
    }

    [Fact]
    public async Task CallReadsCallCustomerWithoutAContactNameAndDialsTheContactPhone()
    {
        var (viewModel, harness) = await Load(JobDtoBuilder.Details1047());

        Assert.True(viewModel.CanCall);
        Assert.Equal("Call Elm Street Bakery", viewModel.CallLabel);

        viewModel.CallCommand.Execute(null);

        harness.Dialer.Received(1).Open("(555) 014-4417");
    }

    [Fact]
    public async Task CallUsesTheContactName_WhenTheJobHasOne()
    {
        var (viewModel, _) = await Load(JobDtoBuilder.Details1047() with { ContactName = "Nina Alvarez", ContactPhone = "(555) 014-6620" });

        Assert.Equal("Call Nina Alvarez", viewModel.CallLabel);
        Assert.Equal("Nina Alvarez", viewModel.ContactValue);
        Assert.Equal("(555) 014-6620", viewModel.ContactSecondary);
    }

    [Fact]
    public async Task CallIsHiddenAndNavigateFillsTheRow_WhenTheDeviceCannotDial()
    {
        var (viewModel, harness) = await Load(JobDtoBuilder.Details1047(), dialerSupported: false);

        Assert.False(viewModel.CanCall);
        Assert.False(viewModel.CallCommand.CanExecute(null));
        viewModel.CallCommand.Execute(null);
        harness.Dialer.DidNotReceive().Open(Arg.Any<string>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task CallIsHidden_WhenTheJobHasNoContactPhone(string? phone)
    {
        var (viewModel, _) = await Load(JobDtoBuilder.Details1047() with { ContactPhone = phone });

        Assert.False(viewModel.CanCall);
        Assert.False(viewModel.HasContact);
    }

    [Fact]
    public async Task NavigateOpensTheMapWithTheSiteAddressAsThePlacemark()
    {
        var (viewModel, harness) = await Load(JobDtoBuilder.Details1047());

        Assert.Equal("Navigate to 88 Elm Street", viewModel.NavigateLabel);
        await viewModel.NavigateCommand.ExecuteAsync(null);

        await harness.Map.Received(1).TryOpenAsync(
            Arg.Is<Placemark>(placemark => placemark.Thoroughfare == "88 Elm Street"),
            Arg.Any<MapLaunchOptions>());
        await harness.Launcher.DidNotReceive().OpenAsync(Arg.Any<Uri>());
        await harness.Launcher.DidNotReceive().TryOpenAsync(Arg.Any<Uri>());
    }

    [Fact]
    public async Task NavigateOpensTheBrowserMapSearch_WhenNoMapsAppIsAvailable()
    {
        var (viewModel, harness) = await Load(JobDtoBuilder.Details1047());
        harness.Map.TryOpenAsync(Arg.Any<Placemark>(), Arg.Any<MapLaunchOptions>()).Returns(false);

        await viewModel.NavigateCommand.ExecuteAsync(null);

        await harness.Launcher.Received(1).OpenAsync(Arg.Is<Uri>(uri => uri.AbsoluteUri == "https://www.bing.com/maps?q=88%20Elm%20Street"));
        await harness.Launcher.DidNotReceive().TryOpenAsync(Arg.Any<Uri>());
    }

    [Fact]
    public async Task TheBrowserMapSearchEscapesEveryReservedCharacterOfTheAddress()
    {
        const string Address = "Unit 4 & 5, #12/B ?x=1";
        var (viewModel, harness) = await Load(JobDtoBuilder.Details1047() with { SiteAddress = Address });
        harness.Map.TryOpenAsync(Arg.Any<Placemark>(), Arg.Any<MapLaunchOptions>()).Returns(false);
        Uri? opened = null;
        harness.Launcher.OpenAsync(Arg.Do<Uri>(uri => opened = uri)).Returns(true);

        await viewModel.NavigateCommand.ExecuteAsync(null);

        Assert.NotNull(opened);
        Assert.Equal("https://www.bing.com/maps?q=Unit%204%20%26%205%2C%20%2312%2FB%20%3Fx%3D1", opened.AbsoluteUri);
        Assert.Equal("https", opened.Scheme);
        Assert.Equal("www.bing.com", opened.Host);
        Assert.Equal("/maps", opened.AbsolutePath);
        Assert.Equal(string.Empty, opened.Fragment);
        Assert.Equal("?q=Unit%204%20%26%205%2C%20%2312%2FB%20%3Fx%3D1", opened.Query);
        Assert.Single(opened.Query.TrimStart('?').Split('&'));
        Assert.Equal(Address, Uri.UnescapeDataString(opened.Query["?q=".Length..]));
    }

    [Fact]
    public async Task NavigateNeverThrowsAndLogsNoAddress_WhenNoBrowserCanOpenTheMapSearch()
    {
        var (viewModel, harness) = await Load(JobDtoBuilder.Details1047());
        harness.Map.TryOpenAsync(Arg.Any<Placemark>(), Arg.Any<MapLaunchOptions>()).Returns(false);
        harness.Launcher.OpenAsync(Arg.Any<Uri>()).ThrowsAsync(new InvalidOperationException("No activity found to handle the intent"));

        await viewModel.NavigateCommand.ExecuteAsync(null);

        var text = harness.Logs.AllText();
        Assert.Contains("jobs.map-fallback", text, StringComparison.Ordinal);
        Assert.Contains("jobs.navigate-failed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Elm Street", text, StringComparison.Ordinal);
        Assert.DoesNotContain("No activity", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NavigateAndCallNeverThrow_WhenThePlatformFails()
    {
        var (viewModel, harness) = await Load(JobDtoBuilder.Details1047());
        harness.Map.TryOpenAsync(Arg.Any<Placemark>(), Arg.Any<MapLaunchOptions>()).ThrowsAsync(new FeatureNotSupportedException());
        harness.Dialer.When(dialer => dialer.Open(Arg.Any<string>())).Do(_ => throw new FeatureNotSupportedException());

        await viewModel.NavigateCommand.ExecuteAsync(null);
        viewModel.CallCommand.Execute(null);

        Assert.Contains("jobs.navigate-failed", harness.Logs.AllText(), StringComparison.Ordinal);
        Assert.Contains("jobs.call-failed", harness.Logs.AllText(), StringComparison.Ordinal);
        Assert.DoesNotContain("014-4417", harness.Logs.AllText(), StringComparison.Ordinal);
        Assert.DoesNotContain("Elm Street", harness.Logs.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheStartsLineUpdatesEveryMinuteWhileThePageIsShown()
    {
        var (viewModel, harness) = await Load(JobDtoBuilder.Details1047());
        viewModel.AppearingCommand.Execute(null);

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(Spaces.Text("Starts in 2_h_47_min"), viewModel.StartsLine);

        harness.Clock.Advance(TimeSpan.FromMinutes(132));
        Assert.Equal(Spaces.Text("Starts in 35_min"), viewModel.StartsLine);

        harness.Clock.Advance(TimeSpan.FromMinutes(35));
        Assert.Equal("Starts now", viewModel.StartsLine);

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal("Was due to start at 1:30 PM", viewModel.StartsLine);

        viewModel.DisappearingCommand.Execute(null);
        harness.Clock.Advance(TimeSpan.FromHours(3));
        Assert.Equal("Was due to start at 1:30 PM", viewModel.StartsLine);
    }

    [Fact]
    public async Task AJobOnAnotherDayShowsItsDateAndNoStartsLine()
    {
        var tomorrow = JobDtoBuilder.Details1047() with
        {
            ScheduledStart = JobDtoBuilder.Local(10, 20, 13, 0),
            ScheduledEnd = JobDtoBuilder.Local(10, 20, 15, 0),
        };
        var later = JobDtoBuilder.Details1047() with
        {
            ScheduledStart = JobDtoBuilder.Local(10, 21, 7, 0),
            ScheduledEnd = JobDtoBuilder.Local(10, 21, 8, 30),
            History =
            [
                JobDtoBuilder.History(
                    JobHistoryAction.Assigned,
                    JobStatus.Unassigned,
                    JobStatus.Assigned,
                    new UserReference(JobDtoBuilder.Dana, "Dana Whitfield"),
                    new UserReference(JobDtoBuilder.Marco, "Marco Reyes"),
                    null,
                    JobDtoBuilder.Local(10, 18, 16, 20)),
            ],
        };

        var (tomorrowView, _) = await Load(tomorrow);
        var (laterView, _) = await Load(later);

        Assert.Equal("Tomorrow 1:00–" + DisplayFormats.ZeroWidthSpace + "3:00 PM", tomorrowView.ScheduleLine);
        Assert.Null(tomorrowView.StartsLine);
        Assert.Equal("Wed 21 Oct, 7:00–" + DisplayFormats.ZeroWidthSpace + "8:30 AM", laterView.ScheduleLine);
        Assert.Equal("Assigned to you by Dana Whitfield on Sun 18 Oct at 4:20 PM", laterView.AssignedLine);
    }

    [Fact]
    public async Task OnTheTwoPaneLayoutTheActionsMoveFromTheDockToTheHero()
    {
        var (viewModel, _) = await Load(JobDtoBuilder.Details1047());

        viewModel.IsInPane = true;

        Assert.False(viewModel.ShowsDock);
        Assert.True(viewModel.ShowsHeroActions);
        Assert.True(viewModel.ShowsPaneHero);
        Assert.False(viewModel.ShowsPushedHeader);
    }

    [Fact]
    public async Task StartJobJoinsNavigateAndCallInTheAssignedDockOnlyWhenTheServerAllowsIt()
    {
        var (allowed, _) = await Load(JobDtoBuilder.Details1047());
        var (refused, _) = await Load(JobDtoBuilder.Details1047() with { Affordances = new JobAffordances([], false) });

        Assert.True(allowed.ShowsDock);
        Assert.True(allowed.ShowsNavigationRow);
        Assert.True(allowed.ShowsStartJob);
        Assert.Equal("Dispatch sees the job as In progress as soon as you start.", allowed.DockHelper);
        Assert.True(refused.ShowsDock);
        Assert.True(refused.ShowsNavigationRow);
        Assert.False(refused.ShowsStartJob);
        Assert.Null(refused.DockHelper);
    }

    private static async Task<(JobDetailsViewModel ViewModel, JobsHarness Harness)> Load(JobDetailsResponse job, bool dialerSupported = true)
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(job)), dialerSupported);
        var viewModel = harness.CreateDetails();
        await viewModel.ShowJobAsync(job.Id);
        return (viewModel, harness);
    }
}
