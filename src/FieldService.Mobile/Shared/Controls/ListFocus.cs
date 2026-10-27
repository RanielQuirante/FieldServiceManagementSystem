using System.Collections;

namespace FieldService.Mobile.Shared.Controls;

public static class ListFocus
{
    public const string HeadingFocusCommand = "FsFocusListHeading";
    public const string HoldFocusCommand = "FsHoldListFocus";

    public static readonly BindableProperty FirstGroupRequestProperty = BindableProperty.CreateAttached(
        "FirstGroupRequest",
        typeof(int),
        typeof(ListFocus),
        0,
        propertyChanged: OnFirstGroupRequested);

    public static readonly BindableProperty SelectedIntoViewRequestProperty = BindableProperty.CreateAttached(
        "SelectedIntoViewRequest",
        typeof(int),
        typeof(ListFocus),
        0,
        propertyChanged: OnSelectedIntoViewRequested);

    public static int GetSelectedIntoViewRequest(BindableObject view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return (int)view.GetValue(SelectedIntoViewRequestProperty);
    }

    public static void SetSelectedIntoViewRequest(BindableObject view, int value)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.SetValue(SelectedIntoViewRequestProperty, value);
    }

    public static object? GroupOf(IEnumerable? groups, object item) =>
        groups?.Cast<object>().FirstOrDefault(group => group is IEnumerable members && members.Cast<object>().Contains(item));

    public static int GetFirstGroupRequest(BindableObject view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return (int)view.GetValue(FirstGroupRequestProperty);
    }

    public static void SetFirstGroupRequest(BindableObject view, int value)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.SetValue(FirstGroupRequestProperty, value);
    }

    public static object? FirstGroup(IEnumerable? groups) => groups?.Cast<object>().FirstOrDefault();

    public static Label? GroupHeading(IEnumerable<object> descendants, object? group)
    {
        ArgumentNullException.ThrowIfNull(descendants);

        return group is null
            ? null
            : descendants.OfType<Label>().FirstOrDefault(label =>
                ReferenceEquals(label.BindingContext, group)
                && SemanticProperties.GetHeadingLevel(label) != SemanticHeadingLevel.None);
    }

    public static bool MoveToHeading(Label heading)
    {
        ArgumentNullException.ThrowIfNull(heading);

        var request = new FocusQuery();
        heading.Handler?.Invoke(HeadingFocusCommand, request);
        if (request.HasFocusWithin == true)
        {
            return true;
        }

        FocusMoves.MoveTo(heading);
        return false;
    }

    private static void OnSelectedIntoViewRequested(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not SelectableItemsView list || (int)newValue == 0 || Equals(oldValue, newValue))
        {
            return;
        }

        list.Dispatcher.Dispatch(() =>
        {
            if (list.SelectedItem is { } item && FocusMoves.IsShown(list))
            {
                list.ScrollTo(item, GroupOf(list.ItemsSource, item), ScrollToPosition.MakeVisible, animate: false);
            }
        });
    }

    private static void OnFirstGroupRequested(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not ItemsView list || (int)newValue == 0 || Equals(oldValue, newValue) || !FocusMoves.IsShown(list))
        {
            return;
        }

        list.Handler?.Invoke(HoldFocusCommand, true);
        list.Dispatcher.Dispatch(() => Attempt(list, FocusMoves.ReadyAttempts, scrolled: false));
    }

    private static void Attempt(ItemsView list, int attemptsLeft, bool scrolled)
    {
        var group = FirstGroup(list.ItemsSource);
        if (group is null || !FocusMoves.IsShown(list))
        {
            list.Handler?.Invoke(HoldFocusCommand, false);
            return;
        }

        if (GroupHeading(list.GetVisualTreeDescendants(), group) is { } heading && FocusMoves.IsReady(heading))
        {
            list.Handler?.Invoke(HoldFocusCommand, false);
            MoveToHeading(heading);
            return;
        }

        if (!scrolled)
        {
            list.ScrollTo(0, groupIndex: 0, position: ScrollToPosition.MakeVisible, animate: false);
        }

        if (attemptsLeft > 1)
        {
            list.Dispatcher.DispatchDelayed(FocusMoves.ReadyRetryDelay, () => Attempt(list, attemptsLeft - 1, scrolled: true));
            return;
        }

        list.Handler?.Invoke(HoldFocusCommand, false);
    }
}
