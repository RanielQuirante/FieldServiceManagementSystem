using System.Xml.Linq;
using FieldService.Mobile.Features.Inspection;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Shared.Screens;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using NSubstitute;

namespace FieldService.Mobile.Tests.Features.Inspection;

public sealed class KeyboardAndReflowTests
{
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";

    [Fact]
    public async Task OnTheChecklistAnOpenKeyboardHidesTheDockAndItsSpacerAndAsksForTheFocusedFieldInView()
    {
        var (viewModel, _) = await OpenChecklistAsync();
        Assert.True(viewModel.ShowsDock);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.IsKeyboardOpen = true;

        Assert.False(viewModel.ShowsDock);
        Assert.False(viewModel.ShowsBarSpacer);
        Assert.Equal(1, viewModel.FocusInViewRequest);
        Assert.Contains(nameof(InspectionViewModel.ShowsDock), changed);

        viewModel.IsKeyboardOpen = false;

        Assert.True(viewModel.ShowsDock);
        Assert.True(viewModel.ShowsBarSpacer);
        Assert.Equal(1, viewModel.FocusInViewRequest);
    }

    [Fact]
    public void OnReviewAndSubmitAnOpenKeyboardHidesTheDockAndItsSpacerAndAsksForTheFocusedFieldInView()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence());
        var viewModel = harness.CreateReview();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.IsKeyboardOpen = true;

        Assert.False(viewModel.ShowsDock);
        Assert.False(viewModel.ShowsBarSpacer);
        Assert.Equal(1, viewModel.FocusInViewRequest);
        Assert.Contains(nameof(ReviewAndSubmitViewModel.ShowsDock), changed);
        Assert.Contains(nameof(ReviewAndSubmitViewModel.ShowsBarSpacer), changed);
    }

    [Fact]
    public async Task CrossingNineHundredOnTheChecklistKeepsFocusAndAsksForTheFocusedControlInViewButOpeningItDoesNot()
    {
        var (viewModel, harness) = await OpenChecklistAsync();
        var title = viewModel.TitleFocusRequest;
        Assert.Equal(0, viewModel.FocusInViewRequest);

        harness.Layout.IsTwoPane.Returns(true);
        harness.Layout.Changed += Raise.Event();
        harness.Layout.IsTwoPane.Returns(false);
        harness.Layout.Changed += Raise.Event();

        Assert.Equal(2, viewModel.FocusInViewRequest);
        Assert.Equal(title, viewModel.TitleFocusRequest);
    }

    [Fact]
    public void CrossingNineHundredOnReviewAndSubmitAsksForTheFocusedControlInView()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence());
        var viewModel = harness.CreateReview();
        viewModel.AppearingCommand.Execute(null);
        Assert.Equal(0, viewModel.FocusInViewRequest);

        harness.Layout.Changed += Raise.Event();

        Assert.Equal(1, viewModel.FocusInViewRequest);
    }

    [Theory]
    [InlineData("Features/Inspection/InspectionPage.xaml", "ChecklistScroll")]
    [InlineData("Features/Inspection/ReviewAndSubmitPage.xaml", "ReviewScroll")]
    public void BothPagesShrinkAboveTheKeyboardHideTheDockWithItAndRevealTheFocusedControl(string path, string scrollName)
    {
        var page = XDocument.Load(Path.Combine([RepositoryPaths.MobileApp, .. path.Split('/')]));
        var root = page.Root!;
        var column = page.Descendants().Single(element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value == "Column");
        var scroll = page.Descendants().Single(element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2009/xaml"))?.Value == scrollName);

        Assert.Equal("True", root.Attribute(_controls + "KeyboardState.ResizesPage")?.Value);
        Assert.Equal("{Binding IsKeyboardOpen}", root.Attribute(_controls + "KeyboardState.IsOpen")?.Value);
        Assert.Equal("None,None,None,SoftInput", column.Attribute("SafeAreaEdges")?.Value);
        Assert.Equal("{Binding ShowsBarSpacer}", page.Descendants(_controls + "SystemBarSpacer").Single().Attribute("IsVisible")?.Value);
        Assert.Equal("{Binding ShowsDock}", page.Descendants(_controls + "ActionDock").Single().Attribute("IsVisible")?.Value);
        Assert.Equal("{Binding FocusInViewRequest}", scroll.Attribute(_controls + "ScrollRequests.FocusedIntoView")?.Value);
        Assert.Equal(BindingMode.OneWayToSource, KeyboardState.IsOpenProperty.DefaultBindingMode);
    }

    [Fact]
    public void AndroidResizesWhileAPageThatAsksForItIsShownAndWatchesTheKeyboardOnlyThen()
    {
        var setup = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Platforms", "Android", "AndroidPlatformSetup.cs"));

        Assert.True(KeyboardState.UsesResize(sheetOpen: false, pageResizes: true));
        Assert.True(KeyboardState.UsesResize(sheetOpen: true, pageResizes: false));
        Assert.False(KeyboardState.UsesResize(sheetOpen: false, pageResizes: false));
        Assert.Contains("PageHandler.Mapper.AppendToMapping(KeyboardState.ResizesPageProperty.PropertyName", setup, StringComparison.Ordinal);
        Assert.Contains("KeyboardState.UsesResize(sheetOpen, _resizingPages > 0) ? Android.Views.SoftInput.AdjustResize : Android.Views.SoftInput.AdjustPan", setup, StringComparison.Ordinal);
        Assert.Contains("platformView.ViewTreeObserver?.AddOnGlobalLayoutListener(watch);", setup, StringComparison.Ordinal);
        Assert.Contains("platformView.ViewTreeObserver?.RemoveOnGlobalLayoutListener(watch);", setup, StringComparison.Ordinal);
        Assert.Contains("ViewCompat.GetRootWindowInsets(view)?.IsVisible(WindowInsetsCompat.Type.Ime()) == true", setup, StringComparison.Ordinal);
        Assert.Contains("KeyboardState.SetIsOpen(target, open);", setup, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFocusedFieldIsRevealedWholeWithItsLabelAndLinesNotJustItsInput()
    {
        var input = new Entry();
        var scroll = new ScrollView { Content = new VerticalStackLayout { new Label(), new VerticalStackLayout { input } } };
        var requests = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Controls", "ScrollRequests.cs"));

        Assert.Null(ScrollRequests.FocusedUnit(scroll));
        ((IView)input).IsFocused = true;

        Assert.Same(input, ScrollRequests.FocusedUnit(scroll));
        Assert.Contains("if (node is FormField or CheckboxRow or OptionRow)", requests, StringComparison.Ordinal);
        Assert.Contains("RevealInScroll(unit);", requests, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(767.6, 587, -1, true)]
    [InlineData(587, 587, -1, false)]
    [InlineData(587, 767.6, -1, false)]
    [InlineData(587, 587, 900, true)]
    [InlineData(587, 600, 1499, true)]
    [InlineData(587, 600, 1500, false)]
    public void TheFocusedControlIsRevealedAgainWheneverTheViewportShrinksAndOnAnyResizeSoonAfterARequest(double oldHeight, double newHeight, long sinceRequest, bool reveals) =>
        Assert.Equal(reveals, ScrollRequests.RevealsAfterResize(oldHeight, newHeight, sinceRequest));

    [Fact]
    public void OnWindowsAFocusedControlWithNoSharedFocusStateIsRevealedByThePlatform16Clear()
    {
        var setup = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Platforms", "Windows", "WindowsPlatformSetup.cs"));
        var focus = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Platforms", "Windows", "KeyboardFocus.cs"));

        Assert.Contains("ScrollViewHandler.CommandMapper.AppendToMapping(ScrollRequests.RevealFocusedCommand, (handler, _, _) => KeyboardFocus.RevealFocusedWithin(handler.PlatformView));", setup, StringComparison.Ordinal);
        Assert.Contains("ScrollRequests.RevealOffset(top, focused.ActualHeight, scroller.VerticalOffset, scroller.ViewportHeight, 0)", focus, StringComparison.Ordinal);
        Assert.Contains("scroller.ChangeView(null, offset, null, true);", focus, StringComparison.Ordinal);
        Assert.Equal(16, ScrollRequests.RevealClearance);
    }

    private static async Task<(InspectionViewModel ViewModel, JobsHarness Harness)> OpenChecklistAsync()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042())));
        harness.Store.RememberJob(new InspectionJob(JobDtoBuilder.Job1042, 1042, "Rooftop HVAC unit not cooling", "Rooftop HVAC service"));
        var viewModel = harness.CreateInspection();
        viewModel.ApplyQueryAttributes(JobsHarness.JobQuery(JobDtoBuilder.Job1042));
        await viewModel.AppearingCommand.ExecuteAsync(null);
        Assert.Equal(ScreenState.Content, viewModel.State);
        return (viewModel, harness);
    }
}
