namespace FieldService.Mobile.Shared.Controls;

public interface IFocusForwarder
{
    VisualElement? FocusTarget { get; }
}

public sealed class FocusQuery
{
    public bool? HasFocusWithin { get; set; }
}

public static class ScreenFocus
{
    public const string KeyboardFocusWithinCommand = "FsKeyboardFocusWithin";

    public static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(250);

    public static readonly BindableProperty IsStartProperty = BindableProperty.CreateAttached(
        "IsStart",
        typeof(bool),
        typeof(ScreenFocus),
        false);

    public static readonly BindableProperty WatchesProperty = BindableProperty.CreateAttached(
        "Watches",
        typeof(bool),
        typeof(ScreenFocus),
        false,
        propertyChanged: OnWatchesChanged);

    public static bool GetIsStart(BindableObject view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return (bool)view.GetValue(IsStartProperty);
    }

    public static void SetIsStart(BindableObject view, bool value)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.SetValue(IsStartProperty, value);
    }

    public static bool GetWatches(BindableObject view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return (bool)view.GetValue(WatchesProperty);
    }

    public static void SetWatches(BindableObject view, bool value)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.SetValue(WatchesProperty, value);
    }

    public static VisualElement? StartTarget(IEnumerable<object> descendants)
    {
        ArgumentNullException.ThrowIfNull(descendants);

        var start = descendants.OfType<VisualElement>().FirstOrDefault(element => GetIsStart(element) && element.IsEnabled && FocusMoves.IsShown(element));
        return start is IFocusForwarder forwarder ? forwarder.FocusTarget : start;
    }

    public static bool? HasKeyboardFocusWithin(VisualElement scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var query = new FocusQuery();
        scope.Handler?.Invoke(KeyboardFocusWithinCommand, query);
        return query.HasFocusWithin;
    }

    public static bool NeedsMove(bool? hasFocusWithin) => hasFocusWithin == false;

    public static bool Ensure(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (!NeedsMove(HasKeyboardFocusWithin(page)) || StartTarget(page.GetVisualTreeDescendants()) is not { } target)
        {
            return false;
        }

        return FocusMoves.MoveKeyboardFocus(target);
    }

    private static void OnWatchesChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not Page page)
        {
            return;
        }

        page.NavigatedTo -= OnNavigatedTo;
        page.Loaded -= OnLoaded;
        if ((bool)newValue)
        {
            page.NavigatedTo += OnNavigatedTo;
            page.Loaded += OnLoaded;
        }
    }

    private static void OnNavigatedTo(object? sender, NavigatedToEventArgs e) => Schedule(sender as Page);

    private static void OnLoaded(object? sender, EventArgs e) => Schedule(sender as Page);

    private static void Schedule(Page? page)
    {
        if (page?.Handler is null)
        {
            return;
        }

        page.Dispatcher.DispatchDelayed(SettleDelay, () => Ensure(page));
    }
}
