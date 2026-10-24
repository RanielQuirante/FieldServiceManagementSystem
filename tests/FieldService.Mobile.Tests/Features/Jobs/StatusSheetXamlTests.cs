using System.Xml.Linq;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Features.Jobs;

public sealed class StatusSheetXamlTests
{
    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2009/xaml";
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";
    private static readonly XNamespace _jobs = "clr-namespace:FieldService.Mobile.Features.Jobs";

    private static readonly HashSet<string> _interactiveTypes = new(StringComparer.Ordinal)
    {
        "Button",
        "ImageButton",
        "Entry",
        "Editor",
        "CheckBox",
        "RadioButton",
    };

    [Fact]
    public void AnOptionRowIsOneRadioButtonNodeWithItsCheckedStateAndClickAction()
    {
        var row = Load("Shared/Controls/OptionRow.xaml");
        var setup = Source("Platforms/Android/AndroidPlatformSetup.cs");
        var handler = Source("Platforms/Android/OptionRowHandler.cs");
        var surface = row.Root!.Elements(_maui + "Border").Single();

        Assert.Equal("android.widget.RadioButton", OptionRow.AndroidClassName);
        Assert.Contains("AddHandler<OptionRow, OptionRowHandler>()", setup, StringComparison.Ordinal);
        Assert.Contains("info.ClassName = OptionRow.AndroidClassName;", handler, StringComparison.Ordinal);
        Assert.Contains("info.Checkable = true;", handler, StringComparison.Ordinal);
        Assert.Contains("info.Checked = row.IsSelected;", handler, StringComparison.Ordinal);
        Assert.Contains("info.CheckedState = row.IsSelected ? CheckedState.True : CheckedState.False;", handler, StringComparison.Ordinal);
        Assert.Contains("info.Enabled = row.IsAvailable;", handler, StringComparison.Ordinal);
        Assert.Contains("info.Clickable = row.IsAvailable;", handler, StringComparison.Ordinal);
        Assert.Contains("AccessibilityNodeInfo.AccessibilityAction.ActionClick", handler, StringComparison.Ordinal);
        Assert.Contains("row.Activate()", handler, StringComparison.Ordinal);
        Assert.Equal("True", surface.Attribute("AutomationProperties.ExcludedWithChildren")?.Value);
        Assert.Equal("True", surface.Attribute("InputTransparent")?.Value);
        Assert.DoesNotContain(row.Descendants(), element => _interactiveTypes.Contains(element.Name.LocalName));
        Assert.Contains(row.Descendants(), element => element.Name.LocalName == "TapGestureRecognizer");
    }

    [Theory]
    [InlineData(true, true, true, "Resume. Carry on where you left off")]
    [InlineData(false, true, false, "Put on hold, selected. Waiting on parts, access or the customer")]
    [InlineData(false, false, false, "Put on hold, not selected. Waiting on parts, access or the customer")]
    public void AnOptionRowIsDescribedByItsTitleAndHelperWithStateWordsOnlyWithoutNativeState(bool native, bool selected, bool resume, string expected)
    {
        var title = resume ? "Resume" : "Put on hold";
        var helper = resume ? "Carry on where you left off" : "Waiting on parts, access or the customer";

        Assert.Equal(expected, OptionRow.Describe(title, helper, selected, isAvailable: true, native));
    }

    [Fact]
    public void ADisabledOptionRowSaysUnavailableAndTakesTheLockAndTheMutedLook()
    {
        Assert.Equal(
            "Complete job, unavailable. Finish the inspection checklist first",
            OptionRow.Describe("Complete job", "Finish the inspection checklist first", isSelected: false, isAvailable: false, nativeState: false));
        Assert.Equal("Complete job. Finish the inspection checklist first", OptionRow.Describe("Complete job", "Finish the inspection checklist first", false, false, true));
        Assert.Equal(new OptionRowLook("FsOptionDisabledFillBrush", "FsHairlineBrush", 1, "FsHairlineBrush", "FsMuted"), OptionRow.LookFor(isSelected: true, isAvailable: false));
        Assert.Equal(new OptionRowLook("FsOptionSelectedFillBrush", "FsTeal700Brush", 1.5, "FsTeal700Brush", "FsInk"), OptionRow.LookFor(isSelected: true, isAvailable: true));
        Assert.Equal(new OptionRowLook("FsTransparentBrush", "FsOptionStrokeBrush", 1, "FsLineInputBrush", "FsInk"), OptionRow.LookFor(isSelected: false, isAvailable: true));
        Assert.Equal("FsIconLock", OptionRow.DisabledIconKey);
    }

    [Fact]
    public void TheSheetListsTheOptionsThenTheReasonThenItsBannerThenItsButtons()
    {
        var sheet = Load("Features/Jobs/UpdateStatusSheet.xaml");
        var root = sheet.Root!.Elements(_maui + "VerticalStackLayout").Single();
        var children = root.Elements().Select(element => element.Name.LocalName).ToList();
        var rows = sheet.Descendants(_controls + "OptionRow").Single();
        var field = sheet.Descendants(_controls + "FormField").Single();
        var banner = sheet.Descendants(_controls + "Banner").Single();
        var confirm = sheet.Descendants(_controls + "PrimaryButton").Single();
        var cancel = sheet.Descendants(_maui + "Button").Single();

        Assert.Equal(["Border", "VerticalStackLayout", "FormField", "Banner", "VerticalStackLayout"], children);
        Assert.DoesNotContain(sheet.Descendants(_maui + "Label"), label => label.Attribute("Text")?.Value == "{x:Static jobs:JobDetailsViewModel.SheetTitle}");
        Assert.Equal("jobs:JobDetailsViewModel", sheet.Root.Attribute(_xaml + "DataType")?.Value);
        Assert.Contains(sheet.Descendants(), element => element.Attribute("BindableLayout.ItemsSource")?.Value == "{Binding StatusOptions}");
        Assert.StartsWith("{Binding SelectStatusOptionCommand", rows.Attribute("Command")?.Value, StringComparison.Ordinal);
        Assert.Equal("{Binding .}", rows.Attribute("CommandParameter")?.Value);
        Assert.Equal("{Binding IsSelected}", rows.Attribute("IsSelected")?.Value);
        Assert.Equal("{Binding ShowsHoldReason}", field.Attribute("IsVisible")?.Value);
        Assert.Equal("True", field.Attribute("IsMultiline")?.Value);
        Assert.Equal("{Binding HoldReason}", field.Attribute("Text")?.Value);
        Assert.Equal("{Binding HoldReasonError}", field.Attribute("ErrorText")?.Value);
        Assert.Equal("{Binding HoldReasonFocusRequest}", field.Attribute("FocusRequest")?.Value);
        Assert.Equal("{Binding IsReasonReadOnly}", field.Attribute("IsReadOnly")?.Value);
        Assert.Equal("{x:Static jobs:JobDetailsViewModel.ReasonMaxLength}", field.Attribute("MaxLength")?.Value);
        Assert.Equal("{x:Static jobs:JobDetailsViewModel.ReasonCountFrom}", field.Attribute("CountFrom")?.Value);
        Assert.Null(field.Attribute("Placeholder"));
        Assert.Equal("Text", field.Attribute("Keyboard")?.Value);
        Assert.Equal("{Binding HasSheetFailure}", banner.Attribute("IsVisible")?.Value);
        Assert.Null(banner.Attribute("ActionText"));
        Assert.Equal("{Binding IsUpdatingStatus}", confirm.Attribute("IsBusy")?.Value);
        Assert.Equal("{Binding ConfirmStatusCommand}", confirm.Attribute("Command")?.Value);
        Assert.Equal("{Binding ConfirmIconKey}", confirm.Attribute("IconKey")?.Value);
        Assert.Equal("{Binding CloseStatusSheetCommand}", cancel.Attribute("Command")?.Value);
        Assert.Equal("{StaticResource FsQuietButton}", cancel.Attribute("Style")?.Value);
        Assert.Equal(500, JobDetailsViewModel.ReasonMaxLength);
        Assert.Equal(400, JobDetailsViewModel.ReasonCountFrom);
    }

    [Fact]
    public void TheDockHoldsStartJobOrUpdateStatusWithItsHelperAndThePageHostsTheSheet()
    {
        var view = Load("Features/Jobs/JobDetailsView.xaml");
        var dock = view.Descendants(_controls + "ActionDock").Single();
        var buttons = dock.Descendants(_controls + "PrimaryButton").ToList();
        var sheet = view.Root!.Elements(_maui + "Grid").Single().Elements(_controls + "BottomSheet").Single();
        var banner = view.Descendants(_controls + "Banner").First();
        var scroll = view.Descendants(_maui + "ScrollView").Single();

        Assert.Equal("{Binding DockHelper}", dock.Attribute("HelperText")?.Value);
        Assert.Equal("{Binding IsStatusSheetOpen}", dock.Attribute("AutomationProperties.ExcludedWithChildren")?.Value);
        var secondary = dock.Descendants(_maui + "Grid").Single(grid => grid.Attribute("IsVisible")?.Value == "{Binding ShowsUpdateStatusSecondary}");
        var secondaryButton = secondary.Elements(_maui + "Button").Single();
        Assert.Equal(["{Binding StartJobCommand}", "{Binding OpenStatusSheetCommand}", "{Binding OpenChecklistCommand}"], buttons.Select(button => button.Attribute("Command")?.Value));
        Assert.Equal(["{Binding ShowsStartJob}", "{Binding ShowsUpdateStatusPrimary}", "{Binding ShowsInspectionAction}"], buttons.Select(button => button.Attribute("IsVisible")?.Value));
        Assert.Equal("Starting…", buttons[0].Attribute("BusyText")?.Value);
        Assert.Equal("{Binding IsStarting}", buttons[0].Attribute("IsBusy")?.Value);
        Assert.Equal("{StaticResource FsIconPlayCircle}", buttons[0].Attribute("IconData")?.Value);
        Assert.Equal("{StaticResource FsIconActivity}", buttons[1].Attribute("IconData")?.Value);
        Assert.Equal("{Binding UpdateStatusFocusRequest}", buttons[1].Attribute("SemanticFocusRequest")?.Value);
        Assert.Equal("{Binding InspectionActionText}", buttons[2].Attribute("Text")?.Value);
        Assert.Equal("{StaticResource FsIconClipboardCheck}", buttons[2].Attribute("IconData")?.Value);
        Assert.Equal("{StaticResource FsSecondaryButton}", secondaryButton.Attribute("Style")?.Value);
        Assert.Equal("{Binding OpenStatusSheetCommand}", secondaryButton.Attribute("Command")?.Value);
        Assert.Equal("{Binding UpdateStatusFocusRequest}", secondaryButton.Attribute(_controls + "ScrollRequests.SemanticFocus")?.Value);
        Assert.Contains(buttons[0], secondary.ElementsBeforeSelf());
        Assert.Contains(buttons[2], secondary.ElementsAfterSelf());
        var overlay = secondary.Elements(_maui + "HorizontalStackLayout").Single();
        var triggers = overlay.Descendants(_maui + "DataTrigger").ToList();
        Assert.Equal(2, triggers.Count);
        Assert.All(triggers, trigger =>
        {
            Assert.Equal("{Binding CanUpdateStatus}", trigger.Attribute("Binding")?.Value);
            Assert.Equal("False", trigger.Attribute("Value")?.Value);
            Assert.Equal("{StaticResource FsDisabledText}", trigger.Elements(_maui + "Setter").Single().Attribute("Value")?.Value);
        });
        Assert.Equal(["Color", "TextColor"], triggers.Select(trigger => trigger.Elements(_maui + "Setter").Single().Attribute("Property")?.Value));
        Assert.Equal("{Binding ShowsPhoneSheet}", sheet.Attribute("IsOpen")?.Value);
        Assert.Equal("{Binding CloseStatusSheetCommand}", sheet.Attribute("CloseCommand")?.Value);
        Assert.Equal("{Binding SheetScrollRequest}", sheet.Attribute("ScrollToEndRequest")?.Value);
        Assert.Single(sheet.Elements(_jobs + "UpdateStatusSheet"));
        Assert.Equal("{Binding HasActionBanner}", banner.Attribute("IsVisible")?.Value);
        Assert.Equal("{Binding ActionBanner.Variant}", banner.Attribute("Variant")?.Value);
        Assert.Null(banner.Attribute("ActionText"));
        Assert.Equal("{Binding ScrollToTopRequest}", scroll.Attribute(_controls + "ScrollRequests.ToTop")?.Value);
        Assert.Equal("{Binding IsStatusSheetOpen}", scroll.Attribute("AutomationProperties.ExcludedWithChildren")?.Value);
    }

    [Fact]
    public void TheTwoPaneLayoutShowsTheSheetAsACentredPanelOverTheWindow()
    {
        var page = Load("Features/Jobs/MyJobsPage.xaml");
        var sheet = page.Root!.Elements(_maui + "Grid").Single().Elements(_controls + "BottomSheet").Single(element => element.Attribute("IsCentred") is not null);

        Assert.Equal("True", sheet.Attribute("IsCentred")?.Value);
        Assert.Equal("{Binding Details.ShowsPaneSheet}", sheet.Attribute("IsOpen")?.Value);
        Assert.Equal("{Binding Details}", sheet.Elements(_jobs + "UpdateStatusSheet").Single().Attribute("BindingContext")?.Value);
        Assert.Equal(BottomSheet.BasePadding, BottomSheet.PaddingFor(48, 0, centred: true));
        Assert.Equal(480d, BottomSheet.CentredWidth);
    }

    [Theory]
    [InlineData(0, 0, 28)]
    [InlineData(24, 0, 52)]
    [InlineData(48, 0, 76)]
    [InlineData(48, 320, 28)]
    public void WithTheKeyboardOpenTheSheetSitsOnItWith28OfPaddingInsteadOfTheNavigationBar(double navigationBar, double keyboard, double bottom)
    {
        Assert.Equal(bottom, BottomSheet.PaddingFor(navigationBar, keyboard, centred: false).Bottom);
    }

    [Fact]
    public void TheSheetIsAtMostTheWindowLessTheStatusBarAnd24AndTheKeyboardAndScrollsBeyond()
    {
        var padding = BottomSheet.PaddingFor(24, 0, false);
        var maximum = BottomSheet.MaxContentHeightFor(915, 54, 0, padding, 0);
        var withKeyboard = BottomSheet.MaxContentHeightFor(915, 54, 320, BottomSheet.PaddingFor(24, 320, false), 0);
        var withHeader = BottomSheet.MaxContentHeightFor(915, 54, 320, BottomSheet.PaddingFor(24, 320, false), 61);
        var sheet = Load("Shared/Controls/BottomSheet.xaml");
        var scroll = sheet.Descendants(_maui + "ScrollView").Single();

        Assert.Equal(915 - 54 - 24 - 10 - 52 - 5 - 18, maximum);
        Assert.Equal(915 - 54 - 24 - 320 - 10 - 28 - 5 - 18, withKeyboard);
        Assert.Equal(withKeyboard - 61, withHeader);
        Assert.Equal(BottomSheet.MinimumContentHeight, BottomSheet.MaxContentHeightFor(300, 54, 320, padding, 0));
        Assert.Equal("{TemplateBinding ContentMaxHeight}", scroll.Attribute("MaximumHeightRequest")?.Value);
        Assert.Equal(0, BottomSheet.KeyboardFrom(915, 400, 515));
        Assert.Equal(320, BottomSheet.KeyboardFrom(915, 80, 515));
        Assert.Equal(0, BottomSheet.KeyboardFrom(0, 0, 0));
    }

    [Fact]
    public void TheHandleAndTitleStayFixedAboveAScrollingAreaThatSpansTheSheetsWidth()
    {
        var sheet = Load("Shared/Controls/BottomSheet.xaml");
        var surface = sheet.Descendants(_maui + "Border").Single(element => element.Attribute(_xaml + "Name")?.Value == "SheetSurface");
        var stack = surface.Elements(_maui + "VerticalStackLayout").Single();
        var parts = stack.Elements().Select(element => element.Attribute(_xaml + "Name")?.Value).ToList();
        var header = stack.Elements().Single(element => element.Attribute(_xaml + "Name")?.Value == "SheetHeader");
        var hairline = stack.Elements().Single(element => element.Attribute(_xaml + "Name")?.Value == "SheetHairline");
        var presenter = sheet.Descendants(_maui + "ContentPresenter").Single();
        var metrics = Load("Shared/Theme/Metrics.xaml").Root!.Elements();
        var colors = Load("Shared/Theme/Colors.xaml").Root!.Elements();

        Assert.Equal(["SheetHandle", "SheetHeader", "SheetHairline", "SheetScroll"], parts);
        Assert.Equal("{TemplateBinding Header}", header.Attribute("Content")?.Value);
        Assert.Equal("{StaticResource FsPaddingSheetHeader}", header.Attribute("Padding")?.Value);
        Assert.Equal("16,0,16,9", Metric(metrics, "FsPaddingSheetHeader"));
        Assert.Equal("0,0,0,18", Metric(metrics, "FsMarginSheetHandle"));
        Assert.Equal("1", hairline.Attribute("HeightRequest")?.Value);
        Assert.Equal("0", hairline.Attribute("Opacity")?.Value);
        Assert.Equal("{StaticResource FsHairlineSoft}", hairline.Attribute("Color")?.Value);
        Assert.Equal("#0F0B1F3A", colors.Single(element => element.Attribute(_xaml + "Key")?.Value == "FsHairlineSoft").Value);
        Assert.Equal("{TemplateBinding ContentPadding}", presenter.Attribute("Padding")?.Value);
        foreach (var padding in new[] { BottomSheet.PaddingFor(0), BottomSheet.PaddingFor(48), BottomSheet.PaddingFor(48, 320, false), BottomSheet.PaddingFor(0, 0, true) })
        {
            Assert.Equal(0, padding.Left);
            Assert.Equal(0, padding.Right);
        }

        Assert.Equal(new Thickness(16, 8, 16, 0), BottomSheet.ContentPaddingFor(hasHeader: true));
        Assert.Equal(new Thickness(16, 0, 16, 0), BottomSheet.ContentPaddingFor(hasHeader: false));
        Assert.Equal(18, BottomSheet.ContentTopUnderHeader + BottomSheet.HairlineHeight + 9);
    }

    [Theory]
    [InlineData(true, 0, false)]
    [InlineData(true, 0.4, true)]
    [InlineData(true, 1, true)]
    [InlineData(true, 240, true)]
    [InlineData(false, 240, false)]
    public void TheHairlineShowsOnlyWhileTheContentIsScrolledFromItsTop(bool hasHeader, double offset, bool shown)
    {
        Assert.Equal(shown, BottomSheet.HairlineAt(hasHeader, offset));
    }

    [Fact]
    public void BothSheetHostsPinTheTitleBlockAsTheFixedHeader()
    {
        var view = Load("Features/Jobs/JobDetailsView.xaml");
        var page = Load("Features/Jobs/MyJobsPage.xaml");
        var title = Load("Features/Jobs/UpdateStatusSheetTitle.xaml");

        Assert.Single(view.Descendants(_controls + "BottomSheet.Header").Elements(_jobs + "UpdateStatusSheetTitle"));
        Assert.Equal(
            "{Binding Details}",
            page.Descendants(_controls + "BottomSheet.Header").Elements(_jobs + "UpdateStatusSheetTitle").Single().Attribute("BindingContext")?.Value);
        var heading = title.Descendants(_maui + "Label").First();
        Assert.Equal("{x:Static jobs:JobDetailsViewModel.SheetTitle}", heading.Attribute("Text")?.Value);
        Assert.Equal("Level2", heading.Attribute("SemanticProperties.HeadingLevel")?.Value);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    public void AnOptionRowKeepsItsContentAt16And14FromTheEdgeWhateverItsStroke(double stroke)
    {
        var padding = OptionRow.PaddingFor(stroke);

        Assert.Equal(16, padding.Left + stroke);
        Assert.Equal(14, padding.Top + stroke);
        Assert.Equal(16, padding.Right + stroke);
        Assert.Equal(14, padding.Bottom);
        Assert.Null(Load("Shared/Controls/OptionRow.xaml").Descendants(_maui + "Border").First().Attribute("Padding"));
    }

    [Theory]
    [InlineData(399, null)]
    [InlineData(0, null)]
    [InlineData(400, "100 characters left")]
    [InlineData(499, "1 character left")]
    [InlineData(500, "No characters left")]
    public void TheCountLineAppearsFrom400Characters(int length, string? expected)
    {
        Assert.Equal(expected, FormField.RemainingText(length, 500, 400));
    }

    [Theory]
    [InlineData(null, "100 characters left", true)]
    [InlineData("100 characters left", "99 characters left", false)]
    [InlineData("1 character left", "No characters left", true)]
    [InlineData("No characters left", "No characters left", false)]
    [InlineData("100 characters left", null, false)]
    public void TheCountIsAnnouncedWhenItAppearsAndAtTheLimitOnly(string? previous, string? current, bool announced)
    {
        Assert.Equal(announced, FormField.AnnouncesCount(previous, current));
    }

    [Fact]
    public void TheReasonIsDescribedByItsLabelHelpAndCount()
    {
        Assert.Equal(
            "Reason for hold. Dispatch sees this reason in the job's status history. 100 characters left",
            FormField.Describe(JobDetailsViewModel.ReasonLabel, JobDetailsViewModel.ReasonHelp, "100 characters left"));
        Assert.Equal("Reason for hold. Dispatch sees this reason in the job's status history.", FormField.Describe(JobDetailsViewModel.ReasonLabel, JobDetailsViewModel.ReasonHelp, null));
        Assert.Equal("Email", FormField.Describe("Email", null, null));
    }

    [Fact]
    public void TheFieldsEditorIsTheMultilineVariantWithTheTextareaHeightsAndTheCountUnderIt()
    {
        var field = Load("Shared/Controls/FormField.xaml");
        var editor = field.Descendants(_maui + "Editor").Single();
        var entry = field.Descendants(_maui + "Entry").Single();
        var metrics = Load("Shared/Theme/Metrics.xaml").Root!.Elements();

        Assert.Equal("{Binding IsMultiline}", editor.Attribute("IsVisible")?.Value);
        Assert.Equal("{Binding IsSingleLine}", entry.Attribute("IsVisible")?.Value);
        Assert.Equal("TextChanges", editor.Attribute("AutoSize")?.Value);
        Assert.Equal("{Binding MaxLength}", editor.Attribute("MaxLength")?.Value);
        Assert.Equal("{Binding IsReadOnly}", editor.Attribute("IsReadOnly")?.Value);
        Assert.Equal("{Binding InputDescription}", editor.Attribute("SemanticProperties.Description")?.Value);
        Assert.Equal("{Binding InputDescription}", entry.Attribute("SemanticProperties.Description")?.Value);
        Assert.Equal("105", Metric(metrics, "FsHeightEditorMinimum"));
        Assert.Equal("169", Metric(metrics, "FsHeightEditorMaximum"));
        var under = field.Root!.Elements(_maui + "VerticalStackLayout").Single().Elements().Skip(2).Select(element => element.Elements(_maui + "Label").LastOrDefault()?.Attribute("Text")?.Value ?? element.Attribute("Text")?.Value).ToList();
        Assert.Equal(["{Binding ErrorText}", "{Binding HelpText}", "{Binding CountText}"], under);
    }

    [Fact]
    public void TheConflictBannerUsesTheWarningSurfaceAndTheAccentTileWithTheRefreshIcon()
    {
        Assert.Equal(
            new BannerLook("FsBannerWarningBrush", "FsWarningBannerStrokeBrush", "FsPaddingBanner", "FsShadowWarning", "FsAccentBrush", "FsTransparentBrush", "FsInk"),
            Banner.LookFor(BannerVariant.Conflict));
        Assert.Equal("FsIconRefresh", Banner.DefaultIconKey(BannerVariant.Conflict));
        var colors = Load("Shared/Theme/Colors.xaml").Root!.Elements();
        Assert.Equal("#66F59E0B", colors.Single(element => element.Attribute(_xaml + "Key")?.Value == "FsWarningBannerStroke").Value);
    }

    [Fact]
    public void TimelineEntriesHangFromTheirFirstLine()
    {
        var timeline = Load("Shared/Controls/Timeline.xaml");
        var entry = timeline.Descendants(_maui + "DataTemplate").Single().Elements(_maui + "Grid").Single();
        var columns = entry.Elements().ToList();
        var metrics = Load("Shared/Theme/Metrics.xaml").Root!.Elements();

        Assert.Equal(3, columns.Count);
        Assert.All(columns, column => Assert.Equal("Start", column.Attribute("VerticalOptions")?.Value));
        Assert.Equal("{StaticResource FsMarginTimelineTime}", columns[0].Attribute("Margin")?.Value);
        Assert.Equal("{StaticResource FsMarginTimelineDot}", columns[1].Attribute("Margin")?.Value);
        Assert.Equal("0,2,0,0", Metric(metrics, "FsMarginTimelineTime"));
        Assert.Equal("0,5,0,0", Metric(metrics, "FsMarginTimelineDot"));
    }

    [Fact]
    public void TheOnHoldReasonSitsUnderTheStatusLineIndentedToItsWordsInAtMostThreeLines()
    {
        var header = Load("Shared/Controls/AppHeader.xaml");
        var reason = header.Descendants(_maui + "Label").Single(label => label.Attribute("Text")?.Value == "{Binding ReasonText}");
        var view = Load("Features/Jobs/JobDetailsView.xaml");
        var details = view.Descendants(_controls + "AppHeader").Single();
        var metrics = Load("Shared/Theme/Metrics.xaml").Root!.Elements();

        Assert.Equal("3", reason.Attribute("MaxLines")?.Value);
        Assert.Equal("TailTruncation", reason.Attribute("LineBreakMode")?.Value);
        Assert.Equal("{StaticResource FsTextMetaOnDark}", reason.Attribute("Style")?.Value);
        Assert.Equal("{StaticResource FsMarginHeroReason}", reason.Attribute("Margin")?.Value);
        Assert.Equal("24,0,0,0", Metric(metrics, "FsMarginHeroReason"));
        Assert.Equal("{StaticResource FsSpace4}", reason.Parent!.Attribute("Spacing")?.Value);
        Assert.Equal("{Binding ReasonLine}", details.Attribute("ReasonText")?.Value);
        Assert.Equal("{Binding TitleFocusRequest}", details.Attribute("SummaryFocusRequest")?.Value);
    }

    [Fact]
    public void ClosingASheetReleasesOnlyTheFieldsThatHoldTheKeyboard()
    {
        var holding = new FakeInput(holdsInput: true);
        var idle = new FakeInput(holdsInput: false);

        var released = BottomSheet.ReleaseInputs([idle, new object(), holding]);

        Assert.Equal(1, released);
        Assert.Equal(1, holding.Releases);
        Assert.Equal(0, idle.Releases);
        Assert.False(holding.HoldsInput);
        Assert.Equal(0, BottomSheet.ReleaseInputs([idle, holding]));
    }

    [Fact]
    public void ASheetLetsGoOfItsFieldBeforeItHidesAndAndroidHidesTheKeyboardOnceItHasClosed()
    {
        var sheet = Source("Shared/Controls/BottomSheet.xaml.cs");
        var field = Source("Shared/Controls/FormField.xaml.cs");
        var setup = Source("Platforms/Android/AndroidPlatformSetup.cs");
        var onOpenChanged = sheet[sheet.IndexOf("private void OnOpenChanged(bool isOpen)", StringComparison.Ordinal)..];
        var softInput = setup[setup.IndexOf("private static void ApplySoftInputMode", StringComparison.Ordinal)..];

        var release = onOpenChanged.IndexOf("ReleaseInputs(this.GetVisualTreeDescendants());", StringComparison.Ordinal);
        Assert.InRange(release, 1, onOpenChanged.IndexOf("IsVisible = isOpen;", StringComparison.Ordinal));
        Assert.Contains("public partial class FormField : ContentView, IReleasesInput", field, StringComparison.Ordinal);
        Assert.Contains("public bool HoldsInput => (Input?.IsFocused ?? false) || (Area?.IsFocused ?? false);", field, StringComparison.Ordinal);
        Assert.Contains("_ = field.HideSoftInputAsync(CancellationToken.None);", field, StringComparison.Ordinal);
        Assert.Contains("field.Unfocus();", field, StringComparison.Ordinal);
        Assert.Contains("ApplySoftInputMode(handler.PlatformView, sheet.IsOpen);", setup, StringComparison.Ordinal);
        var mode = softInput.IndexOf("window.SetSoftInputMode(", StringComparison.Ordinal);
        var hide = softInput.IndexOf("WindowCompat.GetInsetsController(window, decor)?.Hide(WindowInsetsCompat.Type.Ime());", StringComparison.Ordinal);
        Assert.InRange(mode, 1, hide);
        Assert.Contains("if (!sheetOpen && window.DecorView is { } decor)", softInput, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReasonFieldSitsInsideTheSheetThatEveryStatusSheetCloseHides()
    {
        var view = Load("Features/Jobs/JobDetailsView.xaml");
        var page = Load("Features/Jobs/MyJobsPage.xaml");
        var content = Load("Features/Jobs/UpdateStatusSheet.xaml");

        var phone = view.Descendants(_controls + "BottomSheet").Single();
        var pane = page.Descendants(_controls + "BottomSheet").Single(sheet => sheet.Attribute("IsOpen")?.Value == "{Binding Details.ShowsPaneSheet}");
        Assert.Equal("{Binding ShowsPhoneSheet}", phone.Attribute("IsOpen")?.Value);
        Assert.Single(phone.Elements(_jobs + "UpdateStatusSheet"));
        Assert.Single(pane.Elements(_jobs + "UpdateStatusSheet"));
        Assert.Single(content.Descendants(_controls + "FormField"), field => field.Attribute("Text")?.Value == "{Binding HoldReason}");
    }

    private sealed class FakeInput(bool holdsInput) : IReleasesInput
    {
        public bool HoldsInput { get; private set; } = holdsInput;

        public int Releases { get; private set; }

        public void ReleaseInput()
        {
            Releases++;
            HoldsInput = false;
        }
    }

    private static string? Metric(IEnumerable<XElement> metrics, string key) =>
        metrics.Single(element => element.Attribute(_xaml + "Key")?.Value == key).Value;

    private static XDocument Load(string relative) =>
        XDocument.Load(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));
}
