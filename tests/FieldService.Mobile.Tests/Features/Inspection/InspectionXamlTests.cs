using System.Xml.Linq;
using FieldService.Mobile.Features.Inspection;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Shared.Screens;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui.Controls;
using NSubstitute;

namespace FieldService.Mobile.Tests.Features.Inspection;

public sealed class InspectionXamlTests
{
    private const string Confirmation = "I confirm the site is safe and the customer has been informed";
    private const double PhoneControlWidth = 254.095;

    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2009/xaml";
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";

    [Fact]
    public void TheAnswerControlIsThreeEqualOptionsOf52WithTheSpecShapeAndOneButtonEach()
    {
        var control = Load("Shared/Controls/AnswerControl.xaml");
        var options = control.Root!.Elements(_maui + "Grid").Single();
        var cells = options.Elements(_maui + "Grid").ToList();

        Assert.Equal("*,*,*", options.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("{StaticResource FsSpace8}", options.Attribute("ColumnSpacing")?.Value);
        Assert.Equal("{StaticResource FsSpace8}", options.Attribute("RowSpacing")?.Value);
        Assert.Equal("{Binding GroupDescription}", options.Attribute("SemanticProperties.Description")?.Value);
        Assert.Equal(["PassOption", "FailOption", "NotApplicableOption"], cells.Select(cell => cell.Attribute(_xaml + "Name")?.Value));
        Assert.Equal(["0", "1", "2"], cells.Select(cell => cell.Attribute("Grid.Column")?.Value));
        Assert.All(cells, cell =>
        {
            var surface = cell.Elements(_maui + "Border").Single();
            var content = surface.Elements(_maui + "HorizontalStackLayout").Single();
            var button = cell.Elements(_maui + "Button").Single();
            Assert.Equal("{StaticResource FsHeightAnswer}", cell.Attribute("HeightRequest")?.Value);
            Assert.Equal("RoundRectangle 14", surface.Attribute("StrokeShape")?.Value);
            Assert.Equal("1.5", surface.Attribute("StrokeThickness")?.Value);
            Assert.Equal("{StaticResource FsPaddingAnswerOption}", surface.Attribute("Padding")?.Value);
            Assert.Equal("True", surface.Attribute("InputTransparent")?.Value);
            Assert.Equal("{StaticResource FsSpace6}", content.Attribute("Spacing")?.Value);
            Assert.Equal("Center", content.Attribute("HorizontalOptions")?.Value);
            Assert.Equal("18", content.Elements(_controls + "Icon").Single().Attribute("Size")?.Value);
            Assert.Equal("{StaticResource FsTextAnswerOption}", content.Elements(_maui + "Label").Single().Attribute("Style")?.Value);
            Assert.Equal("{StaticResource FsOverlayButton}", button.Attribute("Style")?.Value);
            Assert.Equal("{Binding Command}", button.Attribute("Command")?.Value);
        });
        Assert.Equal(
            ["{x:Static inspection:InspectionResult.Pass}", "{x:Static inspection:InspectionResult.Fail}", "{x:Static inspection:InspectionResult.NotApplicable}"],
            cells.Select(cell => cell.Elements(_maui + "Button").Single().Attribute("CommandParameter")?.Value));
        Assert.Equal(
            ["{Binding PassDescription}", "{Binding FailDescription}", "{Binding NotApplicableDescription}"],
            cells.Select(cell => cell.Elements(_maui + "Button").Single().Attribute("SemanticProperties.Description")?.Value));
        Assert.Equal(["Pass", "Fail", "N/A"], cells.Select(cell => cell.Descendants(_maui + "Label").Single().Attribute("Text")?.Value));
        Assert.Single(cells[0].Elements(_maui + "BoxView"), gloss => gloss.Attribute("Background")?.Value == "{StaticResource FsGlossBrush}");
    }

    [Theory]
    [InlineData(InspectionResult.Pass, false, "FsSurfaceBrush", "FsSecondaryStrokeBrush", "FsShadowAnswerIdle", "FsInk2", false)]
    [InlineData(InspectionResult.Fail, false, "FsSurfaceBrush", "FsSecondaryStrokeBrush", "FsShadowAnswerIdle", "FsInk2", false)]
    [InlineData(InspectionResult.NotApplicable, false, "FsSurfaceBrush", "FsSecondaryStrokeBrush", "FsShadowAnswerIdle", "FsInk2", false)]
    [InlineData(InspectionResult.Pass, true, "FsPassBrush", "FsTeal800Brush", "FsShadowAnswerPass", "FsOnDark", true)]
    [InlineData(InspectionResult.Fail, true, "FsChipFailBrush", "FsDangerBrush", "FsShadowAnswerFail", "FsDangerStrong", false)]
    [InlineData(InspectionResult.NotApplicable, true, "FsBannerPausedTileBrush", "FsMutedBrush", "FsShadowNone", "FsInk", false)]
    public void EachAnswerOptionTakesTheLookOfTheAnswerTable(InspectionResult option, bool selected, string fill, string stroke, string shadow, string text, bool gloss)
    {
        Assert.Equal(new AnswerLook(fill, stroke, shadow, text, gloss), AnswerControl.LookFor(option, selected));
    }

    [Fact]
    public void TheAnswerShadowsAreTheSpecShadows()
    {
        var styles = Load("Shared/Theme/Styles.xaml").Root!.Elements(_maui + "Shadow").ToDictionary(shadow => shadow.Attribute(_xaml + "Key")!.Value);

        Assert.Equal(("{StaticResource FsShadowBrush}", "0,1", "2", "0.06"), Shadow(styles["FsShadowAnswerIdle"]));
        Assert.Equal(("{StaticResource FsTeal700Brush}", "0,8", "18", "0.70"), Shadow(styles["FsShadowAnswerPass"]));
        Assert.Equal(("{StaticResource FsDangerBrush}", "0,6", "14", "0.45"), Shadow(styles["FsShadowAnswerFail"]));
    }

    [Theory]
    [InlineData(InspectionResult.Pass, InspectionResult.Pass, "Pass, selected")]
    [InlineData(InspectionResult.Pass, null, "Pass, not selected")]
    [InlineData(InspectionResult.Fail, InspectionResult.Pass, "Fail, not selected")]
    [InlineData(InspectionResult.NotApplicable, InspectionResult.NotApplicable, "N/A, selected")]
    public void EachAnswerOptionCarriesItsStateInItsDescription(InspectionResult option, InspectionResult? selected, string description)
    {
        Assert.Equal(description, AnswerControl.Describe(option, selected));
    }

    [Fact]
    public void AnOptionKeepsSidePadding6AndTheOptionsStackWhenOneIsNarrowerThanTheWidestLabelPlus4()
    {
        var metrics = Load("Shared/Theme/Metrics.xaml").Root!.Elements().ToDictionary(element => element.Attribute(_xaml + "Key")?.Value ?? string.Empty, element => element.Value);
        var row = AnswerControl.OptionInnerWidth(PhoneControlWidth, stacked: false);
        var fitToWidth = Method(Source("Shared/Controls/AnswerControl.xaml.cs"), "private void FitToWidth(");

        Assert.Equal("6,0", metrics["FsPaddingAnswerOption"]);
        Assert.Equal(6, AnswerControl.OptionPadding);
        Assert.Equal(4, AnswerControl.StackMargin);
        Assert.Equal(64.365, row, 3);
        Assert.Equal(239.095, AnswerControl.OptionInnerWidth(PhoneControlWidth, stacked: true), 3);
        Assert.True(AnswerControl.ShowsIcon(row, 64.365 - 24));
        Assert.False(AnswerControl.ShowsIcon(row, 64.366 - 24));
        Assert.True(AnswerControl.Stacks(64.37, 64.0));
        Assert.False(AnswerControl.Stacks(64.37, 60.19));
        Assert.False(AnswerControl.Stacks(64.5, 60.5));
        Assert.True(AnswerControl.Stacks(64.5, 60.51));
        Assert.Contains("var stacked = Stacks(OptionInnerWidth(Width, stacked: false), widths.Max());", fitToWidth, StringComparison.Ordinal);
        Assert.Contains("var inner = OptionInnerWidth(Width, stacked);", fitToWidth, StringComparison.Ordinal);
        Assert.Equal(1, fitToWidth.Split("Stacks(").Length - 1);
    }

    [Theory]
    [InlineData(254.095, 36.952, false, true)]
    [InlineData(254.095, 46.476, false, false)]
    [InlineData(254.095, 60.19, false, false)]
    [InlineData(254.095, 62.095, true, true)]
    [InlineData(254.095, 64.0, true, true)]
    [InlineData(253.926, 37.037, false, true)]
    [InlineData(253.926, 46.815, false, false)]
    [InlineData(253.926, 59.556, false, false)]
    [InlineData(253.926, 62.222, true, true)]
    [InlineData(253.926, 64.593, true, true)]
    [InlineData(345.524, 36.952, false, true)]
    [InlineData(345.524, 46.476, false, true)]
    [InlineData(345.524, 60.19, false, true)]
    [InlineData(345.524, 62.095, false, true)]
    [InlineData(345.524, 64.0, false, true)]
    public void AtA320PhoneTheOptionsStackWithIconsFrom1Point9AtEveryDensityAndAt411TheyNeverStack(double controlWidth, double widestLabel, bool stacked, bool showsIcons)
    {
        var stacks = AnswerControl.Stacks(AnswerControl.OptionInnerWidth(controlWidth, stacked: false), widestLabel);

        Assert.Equal(stacked, stacks);
        Assert.Equal(showsIcons, AnswerControl.ShowsIcon(AnswerControl.OptionInnerWidth(controlWidth, stacks), widestLabel));
    }

    [Theory]
    [InlineData(36.95, 27.05, 29.33, true)]
    [InlineData(41.52, 30.86, 33.14, false)]
    [InlineData(30.00, 27.05, 41.00, false)]
    public void TheThreeOptionsShowTheirIconsTogetherOrNotAtAll(double pass, double fail, double notApplicable, bool showsIcons)
    {
        var row = AnswerControl.OptionInnerWidth(PhoneControlWidth, stacked: false);
        var fitToWidth = Method(Source("Shared/Controls/AnswerControl.xaml.cs"), "private void FitToWidth(");

        Assert.Equal(showsIcons, AnswerControl.ShowsIcon(row, new[] { pass, fail, notApplicable }.Max()));
        Assert.True(AnswerControl.ShowsIcon(row, Math.Min(pass, Math.Min(fail, notApplicable))));
        Assert.Contains("var showsIcons = ShowsIcon(inner, widths.Max());", fitToWidth, StringComparison.Ordinal);
        Assert.Contains("PassIcon.IsVisible = showsIcons;", fitToWidth, StringComparison.Ordinal);
        Assert.Contains("FailIcon.IsVisible = showsIcons;", fitToWidth, StringComparison.Ordinal);
        Assert.Contains("NotApplicableIcon.IsVisible = showsIcons;", fitToWidth, StringComparison.Ordinal);
        Assert.Equal(1, fitToWidth.Split("ShowsIcon(").Length - 1);
    }

    [Fact]
    public void TheCheckboxRowIsAGlassCardOf56WithATwentyTwoBoxAndTheLineBoxErrorRowUnderIt()
    {
        var row = Load("Shared/Controls/CheckboxRow.xaml");
        var stack = row.Root!.Elements(_maui + "VerticalStackLayout").Single();
        var surface = stack.Elements(_maui + "Border").Single();
        var grid = surface.Elements(_maui + "Grid").Single();
        var box = grid.Elements(_maui + "CheckBox").Single();
        var label = grid.Elements(_maui + "Label").Single();
        var error = stack.Elements(_maui + "Grid").Single();

        Assert.Equal("{StaticResource FsSpace8}", stack.Attribute("Spacing")?.Value);
        Assert.Equal("True", stack.Attribute("AutomationProperties.ExcludedWithChildren")?.Value);
        Assert.Equal("{StaticResource FsGlassCard}", surface.Attribute("Style")?.Value);
        Assert.Equal("{StaticResource FsPaddingItemCard}", surface.Attribute("Padding")?.Value);
        Assert.Equal("{StaticResource FsHeightCheckboxRow}", surface.Attribute("MinimumHeightRequest")?.Value);
        Assert.Equal("Auto,*", grid.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("{StaticResource FsSpace14}", grid.Attribute("ColumnSpacing")?.Value);
        Assert.Equal("{StaticResource FsTeal700}", box.Attribute("Color")?.Value);
        Assert.Equal("22", box.Attribute("WidthRequest")?.Value);
        Assert.Equal("22", box.Attribute("HeightRequest")?.Value);
        Assert.Equal("0,1,0,0", box.Attribute("Margin")?.Value);
        Assert.Equal("Start", box.Attribute("VerticalOptions")?.Value);
        Assert.Equal("True", box.Attribute("InputTransparent")?.Value);
        Assert.Equal("False", box.Attribute("AutomationProperties.IsInAccessibleTree")?.Value);
        Assert.Equal("{StaticResource FsTextCheckboxLabel}", label.Attribute("Style")?.Value);
        Assert.Equal("{Binding HasError}", error.Attribute("IsVisible")?.Value);
        Assert.Equal(BindingMode.TwoWay, CheckboxRow.IsCheckedProperty.DefaultBindingMode);
    }

    [Fact]
    public void TheCheckboxRowIsOneCheckBoxNodeOnAndroidThatTogglesOnClick()
    {
        var setup = Source("Platforms/Android/AndroidPlatformSetup.cs");
        var handler = Source("Platforms/Android/CheckboxRowHandler.cs");

        Assert.Equal("android.widget.CheckBox", CheckboxRow.AndroidClassName);
        Assert.Contains("handlers.AddHandler<CheckboxRow, CheckboxRowHandler>();", setup, StringComparison.Ordinal);
        Assert.Contains("info.ClassName = CheckboxRow.AndroidClassName;", handler, StringComparison.Ordinal);
        Assert.Contains("info.Checkable = true;", handler, StringComparison.Ordinal);
        Assert.Contains("info.CheckedState = row.IsChecked ? CheckedState.True : CheckedState.False;", handler, StringComparison.Ordinal);
        Assert.Contains("AccessibilityNodeInfo.AccessibilityAction.ActionClick", handler, StringComparison.Ordinal);
        Assert.Contains("return row.Toggle();", handler, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false, null, Confirmation)]
    [InlineData(true, false, ReviewAndSubmitViewModel.ConfirmationRequired, Confirmation + ". " + ReviewAndSubmitViewModel.ConfirmationRequired)]
    [InlineData(false, false, null, Confirmation + ", not checked")]
    [InlineData(false, true, null, Confirmation + ", checked")]
    [InlineData(false, false, ReviewAndSubmitViewModel.ConfirmationRequired, Confirmation + ", not checked. " + ReviewAndSubmitViewModel.ConfirmationRequired)]
    public void TheCheckboxRowIsDescribedByItsTextWithItsStateWhereTheNodeLacksItAndItsError(bool nativeState, bool isChecked, string? error, string description)
    {
        Assert.Equal(description, CheckboxRow.Describe(Confirmation, isChecked, error, nativeState));
    }

    [Fact]
    public void AReadingFieldShowsItsUnitInItsOwnColumnAndTheOptionalMarkerIsAMutedSpan()
    {
        var field = Load("Shared/Controls/FormField.xaml");
        var unit = field.Descendants(_maui + "Label").Single(label => label.Attribute("Text")?.Value == "{Binding Unit}");
        var label = field.Descendants(_maui + "Label").Single(element => element.Attribute(_xaml + "Name")?.Value == "FieldLabel");
        var spans = label.Descendants(_maui + "Span").ToList();

        Assert.Equal("2", unit.Attribute("Grid.Column")?.Value);
        Assert.Equal("{StaticResource FsTextReadingUnit}", unit.Attribute("Style")?.Value);
        Assert.Equal("{StaticResource FsMarginReadingUnit}", unit.Attribute("Margin")?.Value);
        Assert.Equal("{Binding IsReading}", unit.Attribute("IsVisible")?.Value);
        Assert.Equal("False", unit.Attribute("AutomationProperties.IsInAccessibleTree")?.Value);
        Assert.Equal(["{Binding Label}", "{Binding OptionalText}"], spans.Select(span => span.Attribute("Text")?.Value));
        Assert.Equal("GeistMedium", spans[1].Attribute("FontFamily")?.Value);
        Assert.Equal("{StaticResource FsMuted}", spans[1].Attribute("TextColor")?.Value);
        Assert.Equal(" Optional", FormField.OptionalMarker);
        Assert.Equal("GeistMonoMedium", FormField.ReadingFontFamily);
    }

    [Theory]
    [InlineData("Supply air temperature", "°F", false, "Supply air temperature in °F")]
    [InlineData("Suction", "psi", false, "Suction in psi")]
    [InlineData("Technician notes", null, true, "Technician notes, optional")]
    [InlineData("Note", " ", true, "Note, optional")]
    [InlineData("Describe the fault", null, false, "Describe the fault")]
    public void AFieldIsNamedByItsLabelItsUnitAndWhetherItIsOptional(string label, string? unit, bool optional, string name)
    {
        Assert.Equal(name, FormField.FieldName(label, unit, optional));
    }

    [Theory]
    [InlineData(InspectionResult.Pass, 8, "8 Pass")]
    [InlineData(InspectionResult.Fail, 1, "1 Fail")]
    [InlineData(InspectionResult.NotApplicable, 0, "0 N/A")]
    [InlineData(InspectionResult.Pass, -1, "Pass")]
    public void AResultChipShowsItsResultWithItsCountWhenItHasOne(InspectionResult result, int count, string label)
    {
        Assert.Equal(label, StatusChip.ResultLabel(result, count));
    }

    [Fact]
    public void AResultChipUsesTheStatusColoursOfItsMeaningAndItsOwnIcon()
    {
        Assert.Equal(("FsChipCompletedBrush", "FsStatusCompletedRingBrush", "FsStatusCompleted", "FsIconCheck"), StatusChip.ResultKeys(InspectionResult.Pass));
        Assert.Equal(("FsChipFailBrush", "FsStatusFailRingBrush", "FsStatusFail", "FsIconX"), StatusChip.ResultKeys(InspectionResult.Fail));
        Assert.Equal(("FsChipCancelledBrush", "FsStatusCancelledRingBrush", "FsStatusCancelled", "FsIconMinus"), StatusChip.ResultKeys(InspectionResult.NotApplicable));
    }

    [Fact]
    public void TheLockedAndSuccessBannersHaveTheirIconsAndTheSuccessBannerItsTealLook()
    {
        Assert.Equal("FsIconLock", Banner.DefaultIconKey(BannerVariant.Locked));
        Assert.Equal("FsIconCheckCircle", Banner.DefaultIconKey(BannerVariant.Success));
        Assert.Equal(Banner.LookFor(BannerVariant.Offline), Banner.LookFor(BannerVariant.Locked));
        Assert.Equal(
            new BannerLook("FsBannerSuccessBrush", "FsSuccessBannerStrokeBrush", "FsPaddingBannerSurface", "FsShadowSuccess", "FsPassBrush", "FsTransparentBrush", "FsOnDark"),
            Banner.LookFor(BannerVariant.Success));
    }

    [Fact]
    public void MyJobsShowsTheSuccessBannerWithoutASecondAnnouncementInBothLayouts()
    {
        var page = Load("Features/Jobs/MyJobsPage.xaml");
        var banners = page.Descendants(_controls + "Banner").Where(banner => banner.Attribute("Variant")?.Value == "Success").ToList();

        Assert.Equal(2, banners.Count);
        Assert.All(banners, banner =>
        {
            Assert.Equal("{Binding HasSuccess}", banner.Attribute("IsVisible")?.Value);
            Assert.Equal("False", banner.Attribute("Announces")?.Value);
            Assert.Equal("{Binding SuccessTitle}", banner.Attribute("Title")?.Value);
            Assert.Equal("{x:Static jobs:MyJobsViewModel.SubmittedMessage}", banner.Attribute("Message")?.Value);
        });
        Assert.Equal("It is now under Completed.", MyJobsViewModel.SubmittedMessage);
    }

    [Theory]
    [InlineData(290, 2)]
    [InlineData(289.9, 1)]
    [InlineData(237, 1)]
    [InlineData(688, 2)]
    public void ReadingsSitTwoUpOnlyWhenEachFieldKeeps140(double width, int columns)
    {
        Assert.Equal(columns, ReadingsLayout.ColumnsFor(width));
    }

    [Fact]
    public void TheChecklistAndTheReviewTakeA720ColumnInTheTwoPaneWindowAndTheWholeWidthOnAPhone()
    {
        var twoPane = Substitute.For<IWindowLayout>();
        twoPane.IsTwoPane.Returns(true);
        var phone = Substitute.For<IWindowLayout>();

        Assert.Equal(720, DesktopColumn.MaxWidth(twoPane));
        Assert.Equal(LayoutOptions.Center, DesktopColumn.Options(twoPane));
        Assert.Equal(double.PositiveInfinity, DesktopColumn.MaxWidth(phone));
        Assert.Equal(LayoutOptions.Fill, DesktopColumn.Options(phone));
        Assert.All(new[] { "Features/Inspection/InspectionPage.xaml", "Features/Inspection/ReviewAndSubmitPage.xaml" }, path =>
        {
            var column = Load(path).Descendants(_maui + "Grid").Single(grid => grid.Attribute(_xaml + "Name")?.Value == "Column");
            Assert.Equal("{Binding ColumnMaxWidth}", column.Attribute("MaximumWidthRequest")?.Value);
            Assert.Equal("{Binding ColumnOptions}", column.Attribute("HorizontalOptions")?.Value);
        });
    }

    [Fact]
    public void AnInspectionItemCardHoldsItsPromptReadingsAnswerAndNotesBoundToTheItem()
    {
        var page = Load("Features/Inspection/InspectionPage.xaml");
        var template = page.Descendants(_maui + "DataTemplate").Single(element => element.Attribute(_xaml + "DataType")?.Value == "inspection:InspectionItemViewModel");
        var card = template.Elements(_maui + "Border").Single();
        var answer = template.Descendants(_controls + "AnswerControl").Single();
        var readings = template.Descendants(_controls + "ReadingsLayout").Single();
        var reading = readings.Descendants(_controls + "FormField").Single();
        var fields = template.Descendants(_controls + "FormField").Where(field => field != reading).ToList();

        var stack = card.Elements(_maui + "VerticalStackLayout").Single();
        var savedReadings = stack.Elements(_maui + "VerticalStackLayout").Single(element => element.Attribute("BindableLayout.ItemsSource")?.Value == "{Binding SavedReadings}");
        Assert.Equal("{StaticResource FsItemCard}", card.Attribute("Style")?.Value);
        Assert.Equal("{Binding ScrollRequest}", card.Attribute(_controls + "ScrollRequests.IntoView")?.Value);
        Assert.Equal("{Binding IsReadOnly}", card.Attribute("AutomationProperties.ExcludedWithChildren")?.Value);
        Assert.Equal("{Binding ShowsSavedReadings}", savedReadings.Attribute("IsVisible")?.Value);
        Assert.All(stack.Elements().Skip(1), part => Assert.StartsWith("{Binding ", part.Attribute("IsVisible")?.Value ?? string.Empty, StringComparison.Ordinal));
        Assert.Equal("{Binding IsEditable}", answer.Attribute("IsVisible")?.Value);
        Assert.Equal("{Binding Result}", answer.Attribute("Result")?.Value);
        Assert.Equal("{Binding SelectCommand}", answer.Attribute("Command")?.Value);
        Assert.Equal("{Binding ShowsEditableReadings}", readings.Attribute("IsVisible")?.Value);
        Assert.Equal("{Binding Unit}", reading.Attribute("Unit")?.Value);
        Assert.Equal("Numeric", reading.Attribute("Keyboard")?.Value);
        Assert.Equal("{Binding Error}", reading.Attribute("ErrorText")?.Value);
        Assert.Equal(["{Binding ShowsNoteField}", "{Binding ShowsFaultField}"], fields.Select(field => field.Attribute("IsVisible")?.Value));
        Assert.All(fields, field => Assert.Equal("{Binding Note}", field.Attribute("Text")?.Value));
        Assert.All(fields, field => Assert.Equal("{x:Static inspection:InspectionItemViewModel.NoteMaxLength}", field.Attribute("MaxLength")?.Value));
        Assert.Equal("True", fields[0].Attribute("IsOptional")?.Value);
        Assert.Equal("{Binding EndNoteCommand}", fields[0].Attribute("UnfocusedCommand")?.Value);
        Assert.Null(fields[1].Attribute("IsOptional"));
        Assert.Equal("{Binding FaultError}", fields[1].Attribute("ErrorText")?.Value);
        Assert.Contains(readings, answer.ElementsBeforeSelf());
        Assert.Contains(fields[1], answer.ElementsAfterSelf());
        Assert.Equal(1000, InspectionItemViewModel.NoteMaxLength);
        Assert.Equal(900, InspectionItemViewModel.NoteCountFrom);
    }

    [Fact]
    public void TheChecklistDockSavesThenOpensTheReviewAndTheReviewDockSubmits()
    {
        var inspection = Load("Features/Inspection/InspectionPage.xaml").Descendants(_controls + "ActionDock").Single();
        var review = Load("Features/Inspection/ReviewAndSubmitPage.xaml").Descendants(_controls + "ActionDock").Single();
        var save = inspection.Elements(_controls + "PrimaryButton").Single();
        var submit = review.Elements(_controls + "PrimaryButton").Single();

        Assert.Equal("{Binding HelperText}", inspection.Attribute("HelperText")?.Value);
        Assert.Equal("{Binding ReviewCommand}", save.Attribute("Command")?.Value);
        Assert.Equal("{Binding IsSaving}", save.Attribute("IsBusy")?.Value);
        Assert.Equal("{x:Static inspection:InspectionViewModel.SavingText}", save.Attribute("BusyText")?.Value);
        Assert.Equal("{Binding HelperText}", review.Attribute("HelperText")?.Value);
        Assert.Equal("{Binding SubmitCommand}", submit.Attribute("Command")?.Value);
        Assert.Equal("{Binding IsSubmitting}", submit.Attribute("IsBusy")?.Value);
        Assert.Equal("{StaticResource FsIconSend}", submit.Attribute("IconData")?.Value);
        Assert.Equal("{Binding SubmitFocusRequest}", submit.Attribute("SemanticFocusRequest")?.Value);
        Assert.Equal("Review and submit", InspectionViewModel.ReviewText);
        Assert.Equal("Submit completed job", ReviewAndSubmitViewModel.SubmitText);
        Assert.Equal("Submitting…", ReviewAndSubmitViewModel.SubmittingText);
    }

    [Fact]
    public void TheReviewHoldsOptionalNotesUpTo2000AndTheConfirmationRowBoundToTheViewModel()
    {
        var page = Load("Features/Inspection/ReviewAndSubmitPage.xaml");
        var notes = page.Descendants(_controls + "FormField").Single();
        var confirmation = page.Descendants(_controls + "CheckboxRow").Single();

        Assert.Equal("True", notes.Attribute("IsOptional")?.Value);
        Assert.Equal("{Binding TechnicianNotes}", notes.Attribute("Text")?.Value);
        Assert.Equal("{x:Static inspection:ReviewAndSubmitViewModel.NotesMaxLength}", notes.Attribute("MaxLength")?.Value);
        Assert.Equal("{x:Static inspection:ReviewAndSubmitViewModel.NotesCountFrom}", notes.Attribute("CountFrom")?.Value);
        Assert.Equal("{x:Static inspection:ReviewAndSubmitViewModel.NotesHelp}", notes.Attribute("HelpText")?.Value);
        Assert.Equal("{x:Static inspection:ReviewAndSubmitViewModel.ConfirmationText}", confirmation.Attribute("Text")?.Value);
        Assert.Equal("{Binding IsConfirmed}", confirmation.Attribute("IsChecked")?.Value);
        Assert.Equal("{Binding ConfirmationError}", confirmation.Attribute("ErrorText")?.Value);
        Assert.Equal("{Binding ConfirmationFocusRequest}", confirmation.Attribute("FocusRequest")?.Value);
        Assert.Equal(Confirmation, ReviewAndSubmitViewModel.ConfirmationText);
        Assert.Equal((2000, 1800), (ReviewAndSubmitViewModel.NotesMaxLength, ReviewAndSubmitViewModel.NotesCountFrom));
    }

    [Fact]
    public void TheSafetyConfirmationScrollsIntoViewWhenItsErrorShows()
    {
        var page = Load("Features/Inspection/ReviewAndSubmitPage.xaml");
        var confirmation = page.Descendants(_controls + "CheckboxRow").Single();
        var notes = page.Descendants(_controls + "FormField").Single();
        var notesCard = notes.Parent!;
        var scrollRequests = Method(Source("Shared/Controls/ScrollRequests.cs"), "private static void OnIntoViewChanged(");

        Assert.Equal("{Binding ConfirmationFocusRequest}", confirmation.Attribute(_controls + "ScrollRequests.IntoView")?.Value);
        Assert.Equal(confirmation.Attribute("FocusRequest")?.Value, confirmation.Attribute(_controls + "ScrollRequests.IntoView")?.Value);
        Assert.Equal("Border", notesCard.Name.LocalName);
        Assert.Equal("{Binding NotesFocusRequest}", notesCard.Attribute(_controls + "ScrollRequests.IntoView")?.Value);
        Assert.Equal("{Binding NotesFocusRequest}", notes.Attribute("FocusRequest")?.Value);
        Assert.Contains("RevealOffset(top, element.Height, scroll.ScrollY, scroll.Height, inset)", scrollRequests, StringComparison.Ordinal);
        Assert.Contains("scroll.ScrollToAsync(0, offset, false)", scrollRequests, StringComparison.Ordinal);
        Assert.Contains("element.SizeChanged += OnResized;", scrollRequests, StringComparison.Ordinal);
        Assert.Contains("element.Dispatcher.Dispatch(Reveal);", scrollRequests, StringComparison.Ordinal);
        Assert.Contains("element.Dispatcher.DispatchDelayed(RevealWindow, () => element.SizeChanged -= OnResized);", scrollRequests, StringComparison.Ordinal);
        Assert.Equal(16, ScrollRequests.RevealClearance);
    }

    [Theory]
    [InlineData(1200, 130, 400, 750, 54, 596.0)]
    [InlineData(500, 130, 400, 750, 54, null)]
    [InlineData(1004, 130, 400, 750, 54, null)]
    [InlineData(1005, 130, 400, 750, 54, 401.0)]
    [InlineData(420, 130, 400, 750, 54, 350.0)]
    [InlineData(40, 130, 400, 750, 54, 0.0)]
    [InlineData(1200, 900, 0, 750, 54, 1130.0)]
    public void ARevealedTargetSitsWhollyInViewWith16Clear(double top, double height, double scrollY, double viewport, double inset, double? offset)
    {
        Assert.Equal(offset, ScrollRequests.RevealOffset(top, height, scrollY, viewport, inset));
    }

    [Fact]
    public void BothPagesForwardTheHardwareBackToTheirViewModel()
    {
        Assert.Contains("protected override bool OnBackButtonPressed() => _viewModel.HandleBackButton();", Source("Features/Inspection/InspectionPage.xaml.cs"), StringComparison.Ordinal);
        Assert.Contains("protected override bool OnBackButtonPressed() => _viewModel.HandleBackButton();", Source("Features/Inspection/ReviewAndSubmitPage.xaml.cs"), StringComparison.Ordinal);
    }

    private static (string?, string?, string?, string?) Shadow(XElement shadow) =>
        (shadow.Attribute("Brush")?.Value, shadow.Attribute("Offset")?.Value, shadow.Attribute("Radius")?.Value, shadow.Attribute("Opacity")?.Value);

    private static string Method(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature);
        var end = source.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        return source[start..end];
    }

    private static XDocument Load(string relative) =>
        XDocument.Load(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')])).ReplaceLineEndings("\n");
}
