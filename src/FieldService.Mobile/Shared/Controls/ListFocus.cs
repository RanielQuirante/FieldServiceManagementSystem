using System.Collections;

namespace FieldService.Mobile.Shared.Controls;

public static class ListFocus
{
    public static readonly BindableProperty FirstGroupRequestProperty = BindableProperty.CreateAttached(
        "FirstGroupRequest",
        typeof(int),
        typeof(ListFocus),
        0,
        propertyChanged: OnFirstGroupRequested);

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

    private static void OnFirstGroupRequested(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not ItemsView list || (int)newValue == 0 || Equals(oldValue, newValue) || !FocusMoves.IsShown(list))
        {
            return;
        }

        list.Dispatcher.Dispatch(() => Attempt(list, FocusMoves.ReadyAttempts, scrolled: false));
    }

    private static void Attempt(ItemsView list, int attemptsLeft, bool scrolled)
    {
        var group = FirstGroup(list.ItemsSource);
        if (group is null || !FocusMoves.IsShown(list))
        {
            return;
        }

        if (GroupHeading(list.GetVisualTreeDescendants(), group) is { } heading && FocusMoves.IsReady(heading))
        {
            FocusMoves.MoveTo(heading);
            return;
        }

        if (!scrolled)
        {
            list.ScrollTo(0, groupIndex: 0, position: ScrollToPosition.MakeVisible, animate: false);
        }

        if (attemptsLeft > 1)
        {
            list.Dispatcher.DispatchDelayed(FocusMoves.ReadyRetryDelay, () => Attempt(list, attemptsLeft - 1, scrolled: true));
        }
    }
}
