using System.Xml.Linq;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui.Controls;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class SheetAccessibilityTests
{
    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2009/xaml";
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";

    [Fact]
    public void OnAndroidThePageHiddenBehindASheetReturnsToTheAccessibilityTreeWhenTheSheetCloses()
    {
        var setup = Source("Platforms/Android/AndroidPlatformSetup.cs");

        Assert.Contains("ViewHandler.ViewMapper.AppendToMapping(AutomationProperties.ExcludedWithChildrenProperty.PropertyName", setup, StringComparison.Ordinal);
        var start = setup.IndexOf("private static void RestoreAccessibilityImportance", StringComparison.Ordinal);
        Assert.True(start > 0, "AndroidPlatformSetup has no RestoreAccessibilityImportance");
        var restore = setup[start..];
        Assert.Contains("AccessibilityExclusion.RestoresImportance(", restore, StringComparison.Ordinal);
        Assert.Contains("ImportantForAccessibility.NoHideDescendants", restore, StringComparison.Ordinal);
        Assert.Contains("ImportantForAccessibility.Auto", restore, StringComparison.Ordinal);
    }

    [Fact]
    public void AnElementIsPutBackInTheTreeOnlyAfterItsExclusionTurnsOffAndOnlyWhenNothingElseDecidesItsPlace()
    {
        var content = new Grid();
        Assert.False(AccessibilityExclusion.RestoresImportance(content));

        AutomationProperties.SetExcludedWithChildren(content, true);
        Assert.False(AccessibilityExclusion.RestoresImportance(content));

        AutomationProperties.SetExcludedWithChildren(content, false);
        Assert.True(AccessibilityExclusion.RestoresImportance(content));

        AutomationProperties.SetIsInAccessibleTree(content, false);
        Assert.False(AccessibilityExclusion.RestoresImportance(content));
        Assert.Throws<ArgumentNullException>(() => AccessibilityExclusion.RestoresImportance(null!));
    }

    [Fact]
    public void ClosingASheetEndsTheInputSessionOfAFieldThatNoLongerHoldsFocusAndReleasesOnlyTheOneThatDoes()
    {
        var holding = new RecordingInput(holdsInput: true);
        var idle = new RecordingInput(holdsInput: false);

        var released = BottomSheet.ReleaseInputs([idle, new object(), holding]);

        Assert.Equal(1, released);
        Assert.Equal(1, holding.Releases);
        Assert.Equal(0, idle.Releases);
        Assert.Equal(1, holding.Ends);
        Assert.Equal(1, idle.Ends);
        Assert.Equal(["end"], idle.Calls);
        Assert.Equal(["release", "end"], holding.Calls);
    }

    [Fact]
    public void AFocusMoveSkipsAControlThatIsHiddenOrHasNoPlatformView()
    {
        var button = new Button();
        var row = new Grid { Children = { button } };
        _ = new ContentPage { Content = new VerticalStackLayout { Children = { row } } };

        Assert.True(FocusMoves.IsShown(button));
        row.IsVisible = false;
        Assert.False(FocusMoves.IsShown(button));
        row.IsVisible = true;
        button.IsVisible = false;
        Assert.False(FocusMoves.IsShown(button));
        Assert.False(FocusMoves.IsShown(null));
        FocusMoves.MoveTo(button);
        FocusMoves.MoveTo(null);
        FocusMoves.MoveSoon(button, () => true);
        FocusMoves.MoveSoon(null, () => true);
        Assert.False(button.IsFocused);
        Assert.Throws<ArgumentNullException>(() => FocusMoves.MoveSoon(button, null!));
    }

    [Fact]
    public void ClosingASheetEndsTheInputSessionOfEveryFieldInItEvenOneThatHasAlreadyLostFocus()
    {
        var sheet = Source("Shared/Controls/BottomSheet.xaml.cs");
        var field = Source("Shared/Controls/FormField.xaml.cs");
        var setup = Source("Platforms/Android/AndroidPlatformSetup.cs");

        var release = sheet[sheet.IndexOf("public static int ReleaseInputs", StringComparison.Ordinal)..];
        Assert.Contains("input.EndInput();", release, StringComparison.Ordinal);
        Assert.Contains("field?.Handler?.Invoke(EndInputCommand);", field, StringComparison.Ordinal);
        Assert.Contains("EntryHandler.CommandMapper.AppendToMapping(FormField.EndInputCommand", setup, StringComparison.Ordinal);
        Assert.Contains("EditorHandler.CommandMapper.AppendToMapping(FormField.EndInputCommand", setup, StringComparison.Ordinal);
        var start = setup.IndexOf("private static void EndInputSession", StringComparison.Ordinal);
        Assert.True(start > 0, "AndroidPlatformSetup has no EndInputSession");
        var end = setup[start..];
        Assert.Contains(".InvokeIsActive(field)", end, StringComparison.Ordinal);
        Assert.Contains("DescendantFocusability.BeforeDescendants", end, StringComparison.Ordinal);
        Assert.Contains("holder.RequestFocus()", end, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTwoPaneHerosUpdateStatusButtonsTakeFocusBackWhenTheSheetCloses()
    {
        var view = Load("Features/Jobs/JobDetailsView.xaml");
        var hero = view.Descendants(_controls + "WrapRowLayout").Single(element => element.Attribute("IsVisible")?.Value == "{Binding ShowsHeroActions}");

        foreach (var shown in new[] { "{Binding ShowsUpdateStatusSecondary}", "{Binding ShowsUpdateStatusPrimary}" })
        {
            var button = hero.Elements(_maui + "Grid").Single(grid => grid.Attribute("IsVisible")?.Value == shown).Elements(_maui + "Button").Single();
            Assert.Equal("{Binding OpenStatusSheetCommand}", button.Attribute("Command")?.Value);
            Assert.Equal("{Binding UpdateStatusFocusRequest}", button.Attribute(_controls + "ScrollRequests.SemanticFocus")?.Value);
        }
    }

    [Fact]
    public void ThePhoneDockRisesAboveAnOpenKeyboardSoUpdateStatusCanTakeFocusWhileTheSheetClosesAndTheKeyboardGoes()
    {
        var view = Load("Features/Jobs/JobDetailsView.xaml");
        var grid = view.Root!.Elements(_maui + "Grid").Single();
        var children = grid.Elements().ToList();
        var dock = grid.Elements(_controls + "ActionDock").Single();
        var sheet = grid.Elements(_controls + "BottomSheet").Single();
        var updateStatus = dock.Descendants().Where(element =>
            element.Attribute(_controls + "ScrollRequests.SemanticFocus")?.Value == "{Binding UpdateStatusFocusRequest}"
            || element.Attribute("SemanticFocusRequest")?.Value == "{Binding UpdateStatusFocusRequest}").ToList();

        Assert.Equal("None,None,None,SoftInput", dock.Attribute("SafeAreaEdges")?.Value);
        Assert.Equal("{Binding IsStatusSheetOpen}", dock.Attribute("AutomationProperties.ExcludedWithChildren")?.Value);
        Assert.Equal(2, updateStatus.Count);
        Assert.Equal("2", sheet.Attribute("Grid.RowSpan")?.Value);
        Assert.True(children.IndexOf(sheet) > children.IndexOf(dock), "the sheet is drawn over the dock, which rises behind it while the keyboard is open");
    }

    [Fact]
    public void AFocusRequestMovesKeyboardFocusAsWellAsScreenReaderFocus()
    {
        var requests = Source("Shared/Controls/ScrollRequests.cs");
        var primary = Source("Shared/Controls/PrimaryButton.xaml.cs");
        var header = Source("Shared/Controls/AppHeader.xaml.cs");

        Assert.Contains("FocusMoves.MoveTo(element)", requests, StringComparison.Ordinal);
        Assert.Contains("FocusMoves.MoveTo(CoreButton)", primary, StringComparison.Ordinal);
        Assert.Contains("FocusMoves.MoveTo(", header, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Controls", "FocusMoves.cs")), "Shared/Controls/FocusMoves.cs is missing");
        var moves = Source("Shared/Controls/FocusMoves.cs");
        Assert.Contains("target.SetSemanticFocus();", moves, StringComparison.Ordinal);
        Assert.Contains("target.Focus();", moves, StringComparison.Ordinal);
    }

    [Fact]
    public void ClosingTheAccountSheetReturnsFocusToTheAccountButtonInBothLayouts()
    {
        var page = Load("Features/Jobs/MyJobsPage.xaml");
        var header = Load("Shared/Controls/AppHeader.xaml");
        var headers = page.Descendants(_controls + "AppHeader").ToList();
        var avatars = header.Descendants(_maui + "Button").Where(button => button.Attribute("Command")?.Value == "{Binding AvatarCommand}").ToList();

        Assert.Equal(2, headers.Count);
        Assert.All(headers, element => Assert.Equal("{Binding AccountFocusRequest}", element.Attribute("AvatarFocusRequest")?.Value));
        Assert.Equal(2, avatars.Count);
        Assert.All(avatars, avatar => Assert.NotNull(avatar.Attribute(_xaml + "Name")));
    }

    private sealed class RecordingInput(bool holdsInput) : IReleasesInput
    {
        public bool HoldsInput { get; private set; } = holdsInput;

        public List<string> Calls { get; } = [];

        public int Releases => Calls.Count(call => call == "release");

        public int Ends => Calls.Count(call => call == "end");

        public void ReleaseInput()
        {
            Calls.Add("release");
            HoldsInput = false;
        }

        public void EndInput() => Calls.Add("end");
    }

    private static XDocument Load(string relative) =>
        XDocument.Load(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));
}
