using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using NSubstitute;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class ListHeadingFocusTests
{
    [Fact]
    public void WhenThePlatformFocusesTheDayGroupsHeaderItemTheHeadingLabelItselfIsNotFocused()
    {
        var (heading, handler) = Heading(platformMoves: true);

        var moved = ListFocus.MoveToHeading(heading);

        Assert.True(moved);
        handler.Received(1).Invoke(ListFocus.HeadingFocusCommand, Arg.Any<object?>());
        handler.DidNotReceive().Invoke(FocusMoves.RevealCommand, Arg.Any<object?>());
        handler.DidNotReceive().Invoke(nameof(IView.Focus), Arg.Any<object?>());
    }

    [Fact]
    public void WhenThePlatformHasNoHeaderItemTheHeadingLabelTakesFocusAsBefore()
    {
        var (heading, handler) = Heading(platformMoves: false);

        var moved = ListFocus.MoveToHeading(heading);

        Assert.False(moved);
        handler.Received(1).Invoke(ListFocus.HeadingFocusCommand, Arg.Any<object?>());
        handler.Received(1).Invoke(FocusMoves.RevealCommand, Arg.Any<object?>());
        handler.Received(1).Invoke(nameof(IView.Focus), Arg.Any<object?>());
    }

    [Fact]
    public void OnWindowsTheDayGroupsHeaderItemTakesFocusAndTheNextTabGoesToTheFirstCard()
    {
        var setup = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Platforms", "Windows", "WindowsPlatformSetup.cs"));
        var items = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Platforms", "Windows", "ListItems.cs"));

        Assert.Contains("LabelHandler.CommandMapper.AppendToMapping(ListFocus.HeadingFocusCommand", setup, StringComparison.Ordinal);
        Assert.Contains("query.HasFocusWithin = ListItems.FocusGroupHeader(handler.PlatformView);", setup, StringComparison.Ordinal);
        Assert.Contains("Ancestor<ListViewHeaderItem>(text) is not { } header", items, StringComparison.Ordinal);
        Assert.Contains("header.IsTabStop = true;", items, StringComparison.Ordinal);
        Assert.Contains("header.Focus(FocusState.Keyboard)", items, StringComparison.Ordinal);
        Assert.Contains("args.Direction != Microsoft.UI.Xaml.Input.FocusNavigationDirection.Next", items, StringComparison.Ordinal);
        Assert.Contains("args.TrySetNewFocusedElement(item);", items, StringComparison.Ordinal);
        Assert.Contains("header.PreviewKeyDown += OnHeaderKeyDown;", items, StringComparison.Ordinal);
        Assert.Contains("e.Handled = item.Focus(FocusState.Keyboard);", items, StringComparison.Ordinal);
        Assert.Contains("list.ContainerFromIndex(0) as ListViewItem", items, StringComparison.Ordinal);
        Assert.Contains("header.IsTabStop = false;", items[items.IndexOf("private static void LeaveHeader", StringComparison.Ordinal)..], StringComparison.Ordinal);
    }

    [Fact]
    public void WhileTheFirstDayGroupIsAboutToTakeFocusWindowsKeepsTheListFromParkingFocusOnTheAvatar()
    {
        var focus = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Controls", "ListFocus.cs"));
        var setup = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Platforms", "Windows", "WindowsPlatformSetup.cs"));
        var items = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Platforms", "Windows", "ListItems.cs"));
        var requested = focus[focus.IndexOf("private static void OnFirstGroupRequested", StringComparison.Ordinal)..];
        var gettingFocus = items[items.IndexOf("private static void OnAnyGettingFocus", StringComparison.Ordinal)..];

        Assert.True(requested.IndexOf("list.Handler?.Invoke(HoldFocusCommand, true);", StringComparison.Ordinal) < requested.IndexOf("list.Dispatcher.Dispatch(", StringComparison.Ordinal));
        Assert.Equal(4, focus.Split("HoldFocusCommand, ").Length - 1);
        Assert.Contains("ViewHandler.ViewCommandMapper.AppendToMapping(ListFocus.HoldFocusCommand", setup, StringComparison.Ordinal);
        Assert.Contains("args.Direction == Microsoft.UI.Xaml.Input.FocusNavigationDirection.None", gettingFocus, StringComparison.Ordinal);
        Assert.Contains("args.NewFocusedElement is not ListViewHeaderItem", gettingFocus, StringComparison.Ordinal);
        Assert.Contains("!IsShown(old)", gettingFocus, StringComparison.Ordinal);
        Assert.Contains("Ancestor<ListViewBase>(old) is { } list", gettingFocus, StringComparison.Ordinal);
        Assert.Contains("Microsoft.UI.Xaml.Input.FocusManager.GettingFocus += OnAnyGettingFocus;", items, StringComparison.Ordinal);
        Assert.Contains("args.TryCancel();", gettingFocus, StringComparison.Ordinal);
    }

    private static (Label Heading, IViewHandler Handler) Heading(bool platformMoves)
    {
        var handler = Substitute.For<IViewHandler>();
        handler.PlatformView.Returns(new object());
        handler.When(h => h.Invoke(nameof(IView.Focus), Arg.Any<object?>()))
            .Do(call => (call[1] as FocusRequest)?.TrySetResult(true));
        if (platformMoves)
        {
            handler.When(h => h.Invoke(ListFocus.HeadingFocusCommand, Arg.Any<object?>()))
                .Do(call => ((FocusQuery)call[1]!).HasFocusWithin = true);
        }

        var heading = new Label { Text = "TODAY · MON 26 OCT" };
        SemanticProperties.SetHeadingLevel(heading, SemanticHeadingLevel.Level2);
        heading.Handler = handler;
        handler.ClearReceivedCalls();
        return (heading, handler);
    }
}
