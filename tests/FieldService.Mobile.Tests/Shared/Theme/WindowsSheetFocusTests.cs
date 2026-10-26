using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class WindowsSheetFocusTests
{
    [Fact]
    public void ThePageBehindASheetLeavesNarratorsTreeWithoutTouchingTabStopsBecauseTheSheetsCycleAlreadyHoldsTab()
    {
        var covered = Source("Platforms/Windows/CoveredSubtrees.cs");

        Assert.Contains("AccessibilityView.Raw", covered, StringComparison.Ordinal);
        Assert.DoesNotContain("IsTabStop", covered, StringComparison.Ordinal);
        Assert.Contains("panel.TabFocusNavigation = KeyboardNavigationMode.Cycle;", Source("Platforms/Windows/SheetKeys.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheJobListsEmptyViewContainerIsNotATabStopSoAReloadLeavesFocusWhereItWas()
    {
        var items = Source("Platforms/Windows/ListItems.cs");

        Assert.Contains("public const string EmptyViewPartName = \"EmptyViewContentControl\";", items, StringComparison.Ordinal);
        Assert.Contains("emptyView.IsTabStop = false;", items, StringComparison.Ordinal);
        Assert.Contains("list.Loaded += OnListLoaded;", items, StringComparison.Ordinal);
    }

    [Fact]
    public void ATapOnTheOverlayNeitherTakesFocusNorPassesItToTheWindowsRootScrollViewer()
    {
        var setup = Source("Platforms/Windows/WindowsPlatformSetup.cs");
        Assert.Contains("ViewHandler.ViewMapper.AppendToMapping(TabStop.IsStopProperty.PropertyName", setup, StringComparison.Ordinal);
        Assert.Contains("KeyboardFocus.KeepPointerFocus(platformView)", setup, StringComparison.Ordinal);

        var focus = Source("Platforms/Windows/KeyboardFocus.cs");
        Assert.Contains("element.IsTabStop = false;", focus, StringComparison.Ordinal);
        Assert.Contains("element.AllowFocusOnInteraction = false;", focus, StringComparison.Ordinal);
        Assert.Contains("element.AddHandler(UIElement.PointerPressedEvent, _keepFocus, false);", focus, StringComparison.Ordinal);
        Assert.Contains("(_, e) => e.Handled = true", focus, StringComparison.Ordinal);
    }

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));
}
