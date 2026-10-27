using System.Windows.Input;
using System.Xml.Linq;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using NSubstitute;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class KeyboardAndNarratorTests
{
    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2009/xaml";
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";

    [Fact]
    public void OpeningASheetPutsKeyboardFocusOnTheSelectedRowThenTheFirstEnabledRowThenTheMarkedButton()
    {
        var hidden = new FakeRow { IsVisible = false };
        var disabled = new FakeRow { IsOperable = false };
        var first = new FakeRow();
        var selected = new FakeRow { IsOn = true };
        var box = new FakeRow { Role = KeyboardRowRole.CheckBox };
        var cancel = new Button();
        BottomSheet.SetInitialFocus(cancel, true);
        var other = new Button();
        _ = new VerticalStackLayout { Children = { hidden, disabled, box, first, selected, other, cancel } };
        object[] descendants = [hidden, disabled, box, first, selected, other, cancel];

        Assert.Same(selected, BottomSheet.InitialFocusTarget(descendants));
        selected.IsOn = false;
        Assert.Same(first, BottomSheet.InitialFocusTarget(descendants));
        first.IsOperable = false;
        selected.IsOperable = false;
        Assert.Same(cancel, BottomSheet.InitialFocusTarget(descendants));
        cancel.IsEnabled = false;
        Assert.Null(BottomSheet.InitialFocusTarget(descendants));
        Assert.False(BottomSheet.GetInitialFocus(other));
        Assert.Throws<ArgumentNullException>(() => BottomSheet.InitialFocusTarget(null!));
        Assert.Throws<ArgumentNullException>(() => BottomSheet.GetInitialFocus(null!));
        Assert.Throws<ArgumentNullException>(() => BottomSheet.SetInitialFocus(null!, true));

        var account = Load("Features/Jobs/AccountSheet.xaml");
        var status = Load("Features/Jobs/UpdateStatusSheet.xaml");
        foreach (var sheet in new[] { account, status })
        {
            var marked = Assert.Single(sheet.Descendants(), element => element.Attribute(_controls + "BottomSheet.InitialFocus")?.Value == "True");
            Assert.Equal("Cancel", marked.Attribute("Text")?.Value);
        }

        var code = Source("Shared/Controls/BottomSheet.xaml.cs");
        Assert.Contains("FocusMoves.MoveKeyboardFocus(target)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SetSemanticFocus", code, StringComparison.Ordinal);
    }

    [Fact]
    public void EscTheOverlayCancelAndBackReachTheSameCloseCommandAndACloseItRefusesClosesNothing()
    {
        var command = Substitute.For<ICommand>();
        command.CanExecute(null).Returns(true);
        Assert.False(BottomSheet.Close(false, command));
        Assert.False(BottomSheet.Close(true, null));
        command.DidNotReceive().Execute(Arg.Any<object?>());
        Assert.True(BottomSheet.Close(true, command));
        command.Received(1).Execute(null);
        command.CanExecute(null).Returns(false);
        Assert.False(BottomSheet.Close(true, command));
        command.Received(1).Execute(null);

        var closed = Substitute.For<IClosableSheet>();
        var open = Substitute.For<IClosableSheet>();
        open.IsOpen.Returns(true);
        Assert.True(BottomSheet.CloseOpenSheet([closed, new object(), open]));
        open.Received(1).RequestClose();
        closed.DidNotReceive().RequestClose();
        Assert.False(BottomSheet.CloseOpenSheet([closed]));
        Assert.Throws<ArgumentNullException>(() => BottomSheet.CloseOpenSheet(null!));

        var template = Load("Shared/Controls/BottomSheet.xaml");
        Assert.Equal("{TemplateBinding CloseCommand}", template.Descendants(_maui + "TapGestureRecognizer").Single().Attribute("Command")?.Value);
        var page = Load("Features/Jobs/MyJobsPage.xaml");
        var details = Load("Features/Jobs/JobDetailsView.xaml");
        var sheets = page.Descendants(_controls + "BottomSheet").Concat(details.Descendants(_controls + "BottomSheet")).Select(sheet => sheet.Attribute("CloseCommand")?.Value).ToList();
        Assert.Equal(["{Binding CloseAccountSheetCommand}", "{Binding Details.CloseStatusSheetCommand}", "{Binding CloseStatusSheetCommand}"], sheets);
        Assert.Contains(Load("Features/Jobs/AccountSheet.xaml").Descendants(_maui + "Button"), button => button.Attribute("Command")?.Value == "{Binding CloseAccountSheetCommand}");
        Assert.Contains(Load("Features/Jobs/UpdateStatusSheet.xaml").Descendants(_maui + "Button"), button => button.Attribute("Command")?.Value == "{Binding CloseStatusSheetCommand}");

        var keys = Source("Platforms/Windows/SheetKeys.cs");
        Assert.Contains("KeyboardNavigationMode.Cycle", keys, StringComparison.Ordinal);
        Assert.Contains("new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = VirtualKey.Escape }", keys, StringComparison.Ordinal);
        Assert.Contains("sheet.RequestClose()", keys, StringComparison.Ordinal);
        Assert.Contains("SheetKeys.Attach(handler.PlatformView, sheet)", Source("Platforms/Windows/WindowsPlatformSetup.cs"), StringComparison.Ordinal);
        var activity = Source("Platforms/Android/MainActivity.cs");
        Assert.Contains("Keycode.Escape", activity, StringComparison.Ordinal);
        Assert.Contains("BottomSheet.CloseOpenSheet(", activity, StringComparison.Ordinal);
    }

    [Fact]
    public void OnWindowsAPageCoveredByASheetLeavesTheAutomationTreeAndTheTabOrderUntilTheSheetCloses()
    {
        var setup = Source("Platforms/Windows/WindowsPlatformSetup.cs");
        Assert.Contains("ViewHandler.ViewMapper.AppendToMapping(AutomationProperties.ExcludedWithChildrenProperty.PropertyName", setup, StringComparison.Ordinal);
        Assert.Contains("CoveredSubtrees.Apply(platformView, AutomationProperties.GetExcludedWithChildren(element) == true)", setup, StringComparison.Ordinal);

        var covered = Source("Platforms/Windows/CoveredSubtrees.cs");
        Assert.Contains("AccessibilityView.Raw", covered, StringComparison.Ordinal);
        Assert.Contains("root.Loaded += OnRootLoaded", covered, StringComparison.Ordinal);
        Assert.Contains("VisualTreeHelper.GetChild(node, index)", covered, StringComparison.Ordinal);
        Assert.Contains("Restore(element, PlatformAutomationProperties.AccessibilityViewProperty, saved.View)", covered, StringComparison.Ordinal);
        Assert.Contains("KeyboardNavigationMode.Cycle", Source("Platforms/Windows/SheetKeys.cs"), StringComparison.Ordinal);

        var page = Load("Features/Jobs/MyJobsPage.xaml");
        Assert.Contains(page.Descendants(_maui + "Grid"), grid => grid.Attribute("AutomationProperties.ExcludedWithChildren")?.Value == "{Binding HasOpenSheet}");
        var details = Load("Features/Jobs/JobDetailsView.xaml");
        Assert.Equal("{Binding IsStatusSheetOpen}", details.Descendants(_maui + "ScrollView").Single().Attribute("AutomationProperties.ExcludedWithChildren")?.Value);
        Assert.Equal("{Binding IsStatusSheetOpen}", details.Descendants(_controls + "ActionDock").Single().Attribute("AutomationProperties.ExcludedWithChildren")?.Value);
    }

    [Fact]
    public void OnAndroidOnlyAnExclusionThatTurnsOnAfterTheContentWasShownKeepsKeyboardFocusOut()
    {
        var overlay = new Grid();
        AutomationProperties.SetExcludedWithChildren(overlay, true);
        Assert.False(AccessibilityExclusion.CoversContent(overlay));

        var page = new Grid();
        Assert.False(AccessibilityExclusion.CoversContent(page));
        AutomationProperties.SetExcludedWithChildren(page, false);
        Assert.False(AccessibilityExclusion.CoversContent(page));
        AutomationProperties.SetExcludedWithChildren(page, true);
        Assert.True(AccessibilityExclusion.CoversContent(page));
        AutomationProperties.SetExcludedWithChildren(page, false);
        Assert.False(AccessibilityExclusion.CoversContent(page));
        Assert.Throws<ArgumentNullException>(() => AccessibilityExclusion.CoversContent(null!));

        var setup = Source("Platforms/Android/AndroidPlatformSetup.cs");
        Assert.Contains("KeepFocusOut(group, AccessibilityExclusion.CoversContent(element))", setup, StringComparison.Ordinal);
        var keep = setup[setup.IndexOf("private static void KeepFocusOut", StringComparison.Ordinal)..];
        Assert.Contains("DescendantFocusability.BlockDescendants", keep, StringComparison.Ordinal);
        Assert.Contains("group.Focusable = false", keep, StringComparison.Ordinal);
        Assert.Contains("group.DescendantFocusability = saved.Value", keep, StringComparison.Ordinal);
        Assert.Contains("group.Focusable = saved.Focusable", keep, StringComparison.Ordinal);
    }

    [Fact]
    public void OnAndroidTheOverlayIsNotATabStopAndOnlyRowsThatCanBeChosenAre()
    {
        var overlay = Load("Shared/Controls/BottomSheet.xaml").Descendants(_maui + "BoxView").First();
        Assert.Equal("{StaticResource FsOverlay}", overlay.Attribute("Color")?.Value);
        Assert.Equal("False", overlay.Attribute(_controls + "TabStop.IsStop")?.Value);
        var view = new BoxView();
        Assert.True(TabStop.GetIsStop(view));
        TabStop.SetIsStop(view, false);
        Assert.False(TabStop.GetIsStop(view));
        Assert.Throws<ArgumentNullException>(() => TabStop.GetIsStop(null!));
        Assert.Throws<ArgumentNullException>(() => TabStop.SetIsStop(null!, false));

        var setup = Source("Platforms/Android/AndroidPlatformSetup.cs");
        Assert.Contains("ViewHandler.ViewMapper.AppendToMapping(TabStop.IsStopProperty.PropertyName", setup, StringComparison.Ordinal);
        Assert.Contains("platformView.Focusable = false", setup, StringComparison.Ordinal);
        Assert.Contains("handler.PlatformView.Focusable = row.IsOperable", Source("Platforms/Android/OptionRowHandler.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void OnWindowsAFocusMoveByCodeFirstScrollsItsTargetWhollyIntoViewWithoutAnimation()
    {
        var moves = Source("Shared/Controls/FocusMoves.cs");
        foreach (var method in new[] { "public static void MoveTo", "public static bool MoveKeyboardFocus" })
        {
            var body = moves[moves.IndexOf(method, StringComparison.Ordinal)..];
            var reveal = body.IndexOf("handler.Invoke(RevealCommand)", StringComparison.Ordinal);
            var focus = body.IndexOf("target.Focus", StringComparison.Ordinal);
            Assert.True(reveal > 0 && reveal < focus, method);
        }

        var setup = Source("Platforms/Windows/WindowsPlatformSetup.cs");
        Assert.Contains("ViewHandler.ViewCommandMapper.AppendToMapping(FocusMoves.RevealCommand", setup, StringComparison.Ordinal);
        Assert.Contains("StartBringIntoView", setup, StringComparison.Ordinal);
        Assert.Contains("AnimationDesired = false", setup, StringComparison.Ordinal);
        Assert.Contains("ScrollRequests.RevealClearance", setup, StringComparison.Ordinal);
        Assert.Equal(16, ScrollRequests.RevealClearance);
        Assert.DoesNotContain("RevealCommand", Source("Platforms/Android/AndroidPlatformSetup.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheJobListsAreNamedJobsAndOnWindowsEachCardAndDayGroupIsOneNamedElement()
    {
        Assert.Equal("Jobs", ListSemantics.JobListName);
        var page = Load("Features/Jobs/MyJobsPage.xaml");
        var lists = page.Descendants(_maui + "CollectionView").ToList();
        Assert.Equal(2, lists.Count);
        Assert.All(lists, list => Assert.Equal("{x:Static controls:ListSemantics.JobListName}", list.Attribute(_controls + "ListSemantics.Name")?.Value));
        var headers = page.Descendants(_maui + "CollectionView.GroupHeaderTemplate").SelectMany(template => template.Descendants(_maui + "Label")).ToList();
        Assert.Equal(2, headers.Count);
        Assert.All(headers, label =>
        {
            Assert.Equal("Level2", label.Attribute("SemanticProperties.HeadingLevel")?.Value);
            Assert.Equal("True", label.Attribute(_controls + "ListSemantics.IsOwnStop")?.Value);
        });

        var view = new CollectionView();
        Assert.Null(ListSemantics.GetName(view));
        ListSemantics.SetName(view, ListSemantics.JobListName);
        Assert.Equal("Jobs", ListSemantics.GetName(view));
        Assert.Throws<ArgumentNullException>(() => ListSemantics.GetName(null!));
        Assert.Throws<ArgumentNullException>(() => ListSemantics.SetName(null!, "Jobs"));

        var items = Source("Platforms/Windows/ListItems.cs");
        Assert.Contains("list.SingleSelectionFollowsFocus = false", items, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(list, name)", items, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(item, view.Description ?? string.Empty)", items, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetAccessibilityView(card, AccessibilityView.Raw)", items, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(header, text.Text ?? string.Empty)", items, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetHeadingLevel(header, HeadingFor(SemanticProperties.GetHeadingLevel(label)))", items, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetAccessibilityView(text, AccessibilityView.Raw)", items, StringComparison.Ordinal);
        Assert.Contains("header.IsTabStop = false", items, StringComparison.Ordinal);
        var setup = Source("Platforms/Windows/WindowsPlatformSetup.cs");
        Assert.Contains("ListItems.AttachList(handler.PlatformView, list)", setup, StringComparison.Ordinal);
        Assert.Contains("ListItems.AttachCard(handler.PlatformView, card)", setup, StringComparison.Ordinal);
        Assert.Contains("ListItems.AttachGroupHeader(handler.PlatformView, label)", setup, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyboardRowsActOnTheKeysOfTheirRoles()
    {
        Assert.True(KeyboardRows.Handles(KeyboardRowRole.RadioButton, KeyboardRowKey.Space));
        Assert.True(KeyboardRows.Handles(KeyboardRowRole.RadioButton, KeyboardRowKey.Enter));
        Assert.True(KeyboardRows.Handles(KeyboardRowRole.CheckBox, KeyboardRowKey.Space));
        Assert.False(KeyboardRows.Handles(KeyboardRowRole.CheckBox, KeyboardRowKey.Enter));
        Assert.False(KeyboardRows.Handles(KeyboardRowRole.RadioButton, KeyboardRowKey.Other));
        Assert.True(KeyboardRows.Acts(KeyboardRowRole.RadioButton, KeyboardRowKey.Space, released: true));
        Assert.False(KeyboardRows.Acts(KeyboardRowRole.RadioButton, KeyboardRowKey.Space, released: false));
        Assert.True(KeyboardRows.Acts(KeyboardRowRole.RadioButton, KeyboardRowKey.Enter, released: false));
        Assert.False(KeyboardRows.Acts(KeyboardRowRole.RadioButton, KeyboardRowKey.Enter, released: true));
        Assert.False(KeyboardRows.Acts(KeyboardRowRole.CheckBox, KeyboardRowKey.Enter, released: false));

        var radio = new FakeRow();
        Assert.True(KeyboardRows.Press(radio, KeyboardRowKey.Space, released: false));
        Assert.Equal(0, radio.Operations);
        Assert.True(KeyboardRows.Press(radio, KeyboardRowKey.Space, released: true));
        Assert.True(KeyboardRows.Press(radio, KeyboardRowKey.Enter, released: false));
        Assert.True(KeyboardRows.Press(radio, KeyboardRowKey.Enter, released: true));
        Assert.Equal(2, radio.Operations);
        Assert.False(KeyboardRows.Press(radio, KeyboardRowKey.Other, released: true));

        var box = new FakeRow { Role = KeyboardRowRole.CheckBox };
        Assert.False(KeyboardRows.Press(box, KeyboardRowKey.Enter, released: false));
        Assert.True(KeyboardRows.Press(box, KeyboardRowKey.Space, released: true));
        Assert.Equal(1, box.Operations);
        box.IsOperable = false;
        Assert.True(KeyboardRows.Press(box, KeyboardRowKey.Space, released: true));
        Assert.Equal(1, box.Operations);
        Assert.Throws<ArgumentNullException>(() => KeyboardRows.Press(null!, KeyboardRowKey.Space, true));
    }

    [Fact]
    public void OnWindowsOptionRowsAreRadioButtonsInAChangeToGroupAndTheConfirmationIsACheckBox()
    {
        var setup = Source("Platforms/Windows/WindowsPlatformSetup.cs");
        Assert.Contains("handlers.AddHandler<OptionRow, KeyboardRowHandler>()", setup, StringComparison.Ordinal);
        Assert.Contains("handlers.AddHandler<CheckboxRow, KeyboardRowHandler>()", setup, StringComparison.Ordinal);
        Assert.Contains("handlers.AddHandler<OptionGroup, OptionGroupHandler>()", setup, StringComparison.Ordinal);

        var rows = Source("Platforms/Windows/KeyboardRowHandler.cs");
        Assert.Contains("AutomationControlType.CheckBox : AutomationControlType.RadioButton", rows, StringComparison.Ordinal);
        Assert.Contains("(PatternInterface.SelectionItem, KeyboardRowRole.RadioButton) => this", rows, StringComparison.Ordinal);
        Assert.Contains("(PatternInterface.Toggle, KeyboardRowRole.CheckBox) => this", rows, StringComparison.Ordinal);
        Assert.Contains("GetNameCore() => _host.Row?.AccessibleName", rows, StringComparison.Ordinal);
        Assert.Contains("GetFullDescriptionCore() => _host.Row?.AccessibleDetail", rows, StringComparison.Ordinal);
        Assert.Contains("GetChildrenCore() => []", rows, StringComparison.Ordinal);
        Assert.Contains("IsTabStop = row.IsOperable", rows, StringComparison.Ordinal);
        Assert.Contains("KeyboardRows.Press(row, KeyOf(e.Key), released: false)", rows, StringComparison.Ordinal);
        Assert.Contains("KeyboardRows.Press(row, KeyOf(e.Key), released: true)", rows, StringComparison.Ordinal);
        Assert.Contains("node is OptionGroupPanel group", rows, StringComparison.Ordinal);

        var group = Source("Platforms/Windows/OptionGroupHandler.cs");
        Assert.Contains("AutomationControlType.Group", group, StringComparison.Ordinal);
        Assert.Contains("(_panel.CrossPlatformLayout as OptionGroup)?.Title", group, StringComparison.Ordinal);
        var legend = Load("Features/Jobs/UpdateStatusSheet.xaml").Descendants(_controls + "OptionGroup").Single();
        Assert.Equal("Change to", legend.Attribute("Title")?.Value);
        Assert.Equal("{Binding StatusOptions}", legend.Attribute("BindableLayout.ItemsSource")?.Value);
        Assert.NotNull(legend.Descendants(_controls + "OptionRow").SingleOrDefault());

        Assert.Contains("OperatingSystem.IsAndroid() || OperatingSystem.IsWindows()", Source("Shared/Controls/OptionRow.xaml.cs"), StringComparison.Ordinal);
        Assert.Contains("OperatingSystem.IsAndroid() || OperatingSystem.IsWindows()", Source("Shared/Controls/CheckboxRow.xaml.cs"), StringComparison.Ordinal);
        Assert.Equal("Put on hold. Waiting on parts, access or the customer", OptionRow.Describe("Put on hold", "Waiting on parts, access or the customer", isSelected: true, isAvailable: true, nativeState: true));
        Assert.Equal(16, OptionRow.CornerRadius);
        Assert.Equal(22, CheckboxRow.CornerRadius);
        Assert.Equal(new Thickness(0, 0, 0, 25), CheckboxRow.InsetFor(95, new Rect(0, 0, 512, 70)));
        Assert.Equal(Thickness.Zero, CheckboxRow.InsetFor(70, new Rect(0, 0, 512, 70)));
        Assert.Equal(Thickness.Zero, CheckboxRow.InsetFor(0, Rect.Zero));

        var options = new OptionGroup { Title = "Change to" };
        Assert.Equal("Change to", options.Title);
        Assert.Empty(options.Rows);
        Assert.Null(options.SelectedRow);
    }

    [Fact]
    public void TextTargetsTakeKeyboardFocusOnWindowsOnlyByCodeAndOnlyUntilTheyLoseIt()
    {
        var details = Load("Features/Jobs/JobDetailsView.xaml");
        var paneTitle = details.Descendants(_maui + "Label").Single(label => label.Attribute("Style")?.Value == "{StaticResource FsTextPaneHeroTitle}");
        Assert.Equal("{Binding TitleFocusRequest}", paneTitle.Attribute(_controls + "ScrollRequests.SemanticFocus")?.Value);
        Assert.Equal("True", paneTitle.Attribute(_controls + "FocusRing.IsOnDark")?.Value);
        Assert.Equal("{Binding TitleFocusRequest}", details.Descendants(_controls + "AppHeader").Single().Attribute("SummaryFocusRequest")?.Value);
        var summary = Load("Shared/Controls/AppHeader.xaml").Descendants(_maui + "Label").Single(label => label.Attribute(_xaml + "Name")?.Value == "SummaryTitleLabel");
        Assert.Equal("True", summary.Attribute(_controls + "FocusRing.IsOnDark")?.Value);

        Assert.Contains("FocusMoves.MoveTo(SummaryTitleLabel)", Source("Shared/Controls/AppHeader.xaml.cs"), StringComparison.Ordinal);
        Assert.Contains("FocusMoves.MoveTo(this)", Source("Shared/Controls/CheckboxRow.xaml.cs"), StringComparison.Ordinal);
        var direct = RepositoryPaths.MobileSources("*.cs")
            .Where(path => !path.EndsWith("FocusMoves.cs", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains(".SetSemanticFocus()", StringComparison.Ordinal))
            .Select(RepositoryPaths.ToRelative)
            .ToList();
        Assert.Empty(direct);

        Assert.Contains("LabelHandler.CommandMapper.PrependToMapping(nameof(IView.Focus), (handler, view, _) => TextTargets.PrepareForFocus(handler.PlatformView, view as BindableObject))", Source("Platforms/Windows/WindowsPlatformSetup.cs"), StringComparison.Ordinal);
        var targets = Source("Platforms/Windows/TextTargets.cs");
        Assert.Contains("text.IsTabStop = true", targets, StringComparison.Ordinal);
        Assert.Contains("text.IsTabStop = false", targets, StringComparison.Ordinal);
        Assert.Contains("text.LostFocus += OnLostFocus", targets, StringComparison.Ordinal);
        Assert.Contains("FocusRing.DarkColorKey", targets, StringComparison.Ordinal);
    }

    [Fact]
    public void PushedPagesHideTheTitleBarBackOnWindowsAndKeepTheirBackOnAndroid()
    {
        Assert.Equal(3, AppShell.PushedRoutes.Count);
        foreach (var page in AppShell.PushedRoutes.Values)
        {
            var relative = page.Namespace!.Replace("FieldService.Mobile.", string.Empty, StringComparison.Ordinal).Replace('.', '/') + "/" + page.Name + ".xaml";
            var behaviour = Load(relative).Root!.Element(_maui + "Shell.BackButtonBehavior")?.Element(_maui + "BackButtonBehavior");
            Assert.NotNull(behaviour);
            Assert.Equal("{OnPlatform WinUI=False, Default=True}", behaviour.Attribute("IsVisible")?.Value);
        }
    }

    [Fact]
    public void OnWindowsAFocusedButtonKeepsItsNormalLookAndItsRingTakesTheColourOfItsFocusedState()
    {
        var dark = Color.FromArgb("#FCD34D");
        var light = Color.FromArgb("#0F766E");

        var onDark = ButtonWithFocusedBorder(dark);
        Assert.Equal(FocusRing.DarkColorKey, FocusRing.KeepNormalLook(onDark, dark));
        var groups = VisualStateManager.GetVisualStateGroups(onDark);
        Assert.DoesNotContain(groups, group => group.Name == FocusRing.FocusStatesGroup);
        Assert.Contains(groups, group => group.Name == "CommonStates");
        Assert.Equal(FocusRing.DarkColorKey, FocusRing.KeepNormalLook(onDark, dark));
        Assert.Null(FocusRing.FocusedColor(onDark));

        var onLight = ButtonWithFocusedBorder(light);
        Assert.Equal(light, FocusRing.FocusedColor(onLight));
        Assert.Equal(FocusRing.LightColorKey, FocusRing.KeepNormalLook(onLight, dark));

        var segment = new Button();
        Assert.Equal(FocusRing.LightColorKey, FocusRing.ColorKeyFor(segment, dark));
        FocusRing.SetIsOnDark(segment, true);
        Assert.True(FocusRing.GetIsOnDark(segment));
        Assert.Equal(FocusRing.DarkColorKey, FocusRing.KeepNormalLook(segment, dark));
        FocusRing.SetIsOnDark(segment, false);
        Assert.Equal(FocusRing.LightColorKey, FocusRing.ColorKeyFor(segment, dark));
        Assert.Throws<ArgumentNullException>(() => FocusRing.KeepNormalLook(null!, dark));
        Assert.Throws<ArgumentNullException>(() => FocusRing.GetIsOnDark(null!));

        var segments = Source("Shared/Controls/SegmentedControl.xaml.cs");
        Assert.Contains("FocusRing.SetIsOnDark(button, IsOnDark)", segments, StringComparison.Ordinal);
        var setup = Source("Platforms/Windows/WindowsPlatformSetup.cs");
        Assert.Contains("new[] { FocusRingMappingKey, nameof(VisualElement.Style), FocusRing.IsOnDarkProperty.PropertyName }", setup, StringComparison.Ordinal);
        Assert.Contains("FocusVisuals.Apply(handler.PlatformView, FocusRing.KeepNormalLook(button, FocusVisuals.DarkFocus), FocusVisuals.ControlMargin)", setup, StringComparison.Ordinal);
        var visuals = Source("Platforms/Windows/FocusVisuals.cs");
        Assert.Contains("element.UseSystemFocusVisuals = true", visuals, StringComparison.Ordinal);
        Assert.Contains("element.FocusVisualPrimaryThickness = new PlatformThickness(FocusRing.Thickness)", visuals, StringComparison.Ordinal);
        Assert.Contains("element.FocusVisualSecondaryThickness = new PlatformThickness(0)", visuals, StringComparison.Ordinal);
        Assert.Contains("element.FocusVisualSecondaryBrush = new PlatformBrush(Microsoft.UI.Colors.Transparent)", visuals, StringComparison.Ordinal);
        Assert.Contains("element.FocusVisualMargin = margin", visuals, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRingSitsTwoOutsideTheControlAndItsCornersStayConcentric()
    {
        Assert.Equal(2, FocusRing.Thickness);
        Assert.Equal(2, FocusRing.Gap);
        Assert.Equal(-4, FocusRing.ControlMargin);
        Assert.Equal(new Thickness(2), FocusRing.MarginAround(new Thickness(6)));
        Assert.Equal(new Thickness(-4), FocusRing.MarginAround(Thickness.Zero));
        Assert.Equal(new Thickness(-4, -4, -4, 21), FocusRing.MarginAround(new Thickness(0, 0, 0, 25)));
        Assert.Equal(24, FocusRing.ContainerRadius(JobCard.CompactRadius, 2));
        Assert.Equal(28, FocusRing.ContainerRadius(JobCard.Radius, 2));
        Assert.Equal(18, JobCard.CompactRadius);
        Assert.Equal(22, JobCard.Radius);
        Assert.True(FocusRing.IsEven(new Thickness(6)));
        Assert.False(FocusRing.IsEven(new Thickness(16, 6)));
        var (inset, outside) = FocusRing.EvenInset(new Thickness(16, 6));
        Assert.Equal(new Thickness(6), inset);
        Assert.Equal(new Thickness(10, 0, 10, 0), outside);

        var items = Source("Platforms/Windows/ListItems.cs");
        Assert.Contains("FocusRing.EvenInset(layout.Padding)", items, StringComparison.Ordinal);
        Assert.Contains("FocusRing.ContainerRadius(view.IsCompact ? JobCard.CompactRadius : JobCard.Radius, margin.Left)", items, StringComparison.Ordinal);
        Assert.Contains("FocusVisuals.Round(this, row.FocusCornerRadius)", Source("Platforms/Windows/KeyboardRowHandler.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidKeepsItsFocusedStrokeOnEveryButtonStyle()
    {
        var styles = Load("Shared/Theme/Styles.xaml");
        var focused = styles.Descendants(_maui + "VisualState").Where(state => state.Attribute(_xaml + "Name")?.Value == FocusRing.FocusedState).ToList();
        Assert.Equal(7, focused.Count);
        Assert.All(focused, state =>
        {
            var setters = state.Descendants(_maui + "Setter").ToDictionary(setter => setter.Attribute("Property")!.Value, setter => setter.Attribute("Value")!.Value);
            Assert.Contains(setters["BorderColor"], new[] { "{StaticResource FsFocus}", "{StaticResource FsFocusOnDark}" });
            Assert.Equal("2", setters["BorderWidth"]);
        });
        Assert.All(focused, state => Assert.Equal(FocusRing.FocusStatesGroup, state.Parent!.Attribute(_xaml + "Name")?.Value));
    }

    [Fact]
    public void ControlsFocusedByCodeAreNeverRebuiltByTheirDisabledLookSoTheFirstKeyAfterTheMoveReachesThem()
    {
        var details = Load("Features/Jobs/JobDetailsView.xaml");
        var styles = Load("Shared/Theme/Styles.xaml");
        var targets = details.Descendants().Where(element =>
            element.Attribute(_controls + "ScrollRequests.SemanticFocus")?.Value is "{Binding UpdateStatusFocusRequest}"
            || element.Attribute("SemanticFocusRequest")?.Value is "{Binding UpdateStatusFocusRequest}" or "{Binding StartJobFocusRequest}").ToList();
        Assert.Equal(5, targets.Count);
        foreach (var target in targets.Where(element => element.Name == _maui + "Button"))
        {
            var key = target.Attribute("Style")!.Value.Replace("{StaticResource ", string.Empty, StringComparison.Ordinal).TrimEnd('}');
            for (var style = StyleNamed(styles, key); style is not null; style = BasedOn(styles, style))
            {
                Assert.DoesNotContain(style.Descendants(_maui + "Setter"), setter => setter.Attribute("Property")?.Value == "Shadow" && setter.Attribute("Value")?.Value == "{x:Null}");
            }
        }

        Assert.Contains(targets, element => element.Name == _controls + "PrimaryButton");
        var primary = Load("Shared/Controls/PrimaryButton.xaml");
        Assert.DoesNotContain(primary.Descendants(_maui + "Setter"), setter => setter.Attribute("Property")?.Value == "Shadow" && setter.Attribute("Value")?.Value == "{x:Null}");
        Assert.Contains("FocusMoves.MoveTo(element)", Source("Shared/Controls/ScrollRequests.cs"), StringComparison.Ordinal);
        Assert.Contains("FocusMoves.MoveTo(CoreButton)", Source("Shared/Controls/PrimaryButton.xaml.cs"), StringComparison.Ordinal);
    }

    private static Button ButtonWithFocusedBorder(Color focus)
    {
        var button = new Button();
        var groups = VisualStateManager.GetVisualStateGroups(button);
        groups.Add(new VisualStateGroup { Name = "CommonStates", States = { new VisualState { Name = "Normal" } } });
        var focused = new VisualState { Name = FocusRing.FocusedState };
        focused.Setters.Add(new Setter { Property = Button.BorderColorProperty, Value = focus });
        focused.Setters.Add(new Setter { Property = Button.BorderWidthProperty, Value = 2d });
        groups.Add(new VisualStateGroup { Name = FocusRing.FocusStatesGroup, States = { new VisualState { Name = "Unfocused" }, focused } });
        return button;
    }

    private static XElement? StyleNamed(XDocument styles, string key) =>
        styles.Root!.Elements(_maui + "Style").SingleOrDefault(style => style.Attribute(_xaml + "Key")?.Value == key);

    private static XElement? BasedOn(XDocument styles, XElement style) =>
        style.Attribute("BasedOn")?.Value is { } basedOn
            ? StyleNamed(styles, basedOn.Replace("{StaticResource ", string.Empty, StringComparison.Ordinal).TrimEnd('}'))
            : null;

    private static XDocument Load(string relative) =>
        XDocument.Load(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));

    private sealed class FakeRow : ContentView, IKeyboardRow
    {
        public KeyboardRowRole Role { get; init; } = KeyboardRowRole.RadioButton;

        public string AccessibleName => "Put on hold. Waiting on parts, access or the customer";

        public string? AccessibleDetail => null;

        public bool IsOn { get; set; }

        public bool IsOperable { get; set; } = true;

        public double FocusCornerRadius => OptionRow.CornerRadius;

        public Thickness FocusInset => Thickness.Zero;

        public int Operations { get; private set; }

        public bool Operate()
        {
            Operations++;
            return true;
        }
    }
}
