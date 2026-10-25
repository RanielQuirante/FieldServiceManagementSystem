using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Screens;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui.Devices;

namespace FieldService.Mobile.Tests.Features.Jobs;

public sealed class WindowsFocusMoveTests
{
    [Fact]
    public async Task OnWindowsClosingTheSheetWhileUpdateStatusIsDisabledMovesFocusToTheJobTitle()
    {
        var (viewModel, harness) = await OpenAsync(DevicePlatform.WinUI);
        viewModel.OpenStatusSheetCommand.Execute(null);
        harness.SetOnline(false);
        Assert.True(viewModel.IsStatusSheetOpen);
        Assert.False(viewModel.CanUpdateStatus);

        viewModel.CloseStatusSheetCommand.Execute(null);

        Assert.False(viewModel.IsStatusSheetOpen);
        Assert.Equal(1, viewModel.TitleFocusRequest);
        Assert.Equal(0, viewModel.UpdateStatusFocusRequest);
    }

    [Fact]
    public async Task OnAndroidTheClosedSheetStillReturnsFocusToUpdateStatusWhichTalkBackCanFocusWhileDisabled()
    {
        var (viewModel, harness) = await OpenAsync(DevicePlatform.Android);
        viewModel.OpenStatusSheetCommand.Execute(null);
        harness.SetOnline(false);

        viewModel.CloseStatusSheetCommand.Execute(null);

        Assert.Equal(1, viewModel.UpdateStatusFocusRequest);
        Assert.Equal(0, viewModel.TitleFocusRequest);
    }

    [Fact]
    public async Task OnWindowsAnEnabledTriggerTakesTheFocusBackAfterEveryClosePath()
    {
        var (viewModel, _) = await OpenAsync(DevicePlatform.WinUI);
        viewModel.OpenStatusSheetCommand.Execute(null);
        viewModel.CloseStatusSheetCommand.Execute(null);
        viewModel.OpenStatusSheetCommand.Execute(null);
        Assert.True(viewModel.CloseSheet());

        Assert.Equal(2, viewModel.UpdateStatusFocusRequest);
        Assert.Equal(0, viewModel.TitleFocusRequest);
        Assert.False(JobDetailsViewModel.FocusesDisabledTrigger(DevicePlatform.WinUI));
        Assert.True(JobDetailsViewModel.FocusesDisabledTrigger(DevicePlatform.Android));
    }

    private static async Task<(JobDetailsViewModel ViewModel, JobsHarness Harness)> OpenAsync(DevicePlatform platform)
    {
        var job = JobDtoBuilder.Details1042();
        var harness = new JobsHarness(new StubHttpMessageHandler((_, _) => Task.FromResult(JobDtoBuilder.Json(job))), platform: platform);
        var viewModel = harness.CreateDetails();
        viewModel.AppearingCommand.Execute(null);
        await viewModel.ShowJobAsync(job.Id);
        Assert.Equal(ScreenState.Content, viewModel.State);
        return (viewModel, harness);
    }
}
