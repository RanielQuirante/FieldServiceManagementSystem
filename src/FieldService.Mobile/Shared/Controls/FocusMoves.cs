namespace FieldService.Mobile.Shared.Controls;

public static class FocusMoves
{
    public const string RevealCommand = "FsRevealForFocus";
    public const string FocusWithinCommand = "FsFocusWithin";
    public const int ReadyAttempts = 8;

    public static readonly TimeSpan ReadyRetryDelay = TimeSpan.FromMilliseconds(100);

    public static bool? HasFocusWithin(VisualElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var query = new FocusQuery();
        element.Handler?.Invoke(FocusWithinCommand, query);
        return query.HasFocusWithin;
    }

    public static bool IsReady(VisualElement? target) =>
        target is { Handler: not null, IsLoaded: true, Width: > 0, Height: > 0 } && IsShown(target);

    public static void MoveWhenReady(VisualElement target, int attempts = ReadyAttempts)
    {
        ArgumentNullException.ThrowIfNull(target);

        target.Dispatcher.Dispatch(() => AttemptWhenReady(target, attempts));
    }

    private static void AttemptWhenReady(VisualElement target, int attemptsLeft)
    {
        if (IsReady(target))
        {
            MoveTo(target);
            return;
        }

        if (attemptsLeft > 1)
        {
            target.Dispatcher.DispatchDelayed(ReadyRetryDelay, () => AttemptWhenReady(target, attemptsLeft - 1));
        }
    }

    public static bool IsShown(Element? element)
    {
        if (element is null)
        {
            return false;
        }

        for (var current = element; current is not null; current = current.Parent)
        {
            if (current is VisualElement { IsVisible: false })
            {
                return false;
            }
        }

        return true;
    }

    public static void MoveTo(VisualElement? target)
    {
        if (target?.Handler is not { } handler || !IsShown(target))
        {
            return;
        }

        handler.Invoke(RevealCommand);
        target.SetSemanticFocus();
        target.Focus();
    }

    public static bool MoveKeyboardFocus(VisualElement? target)
    {
        if (target?.Handler is not { } handler || !IsShown(target))
        {
            return false;
        }

        handler.Invoke(RevealCommand);
        return target.Focus();
    }

    public static bool RevealThenFocus(VisualElement container, VisualElement input)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(input);

        container.Handler?.Invoke(RevealCommand);
        return input.Focus();
    }

    public static void MoveSoon(VisualElement? target, Func<bool> stillWanted)
    {
        ArgumentNullException.ThrowIfNull(stillWanted);

        if (target?.Handler is null)
        {
            return;
        }

        target.Dispatcher.Dispatch(() =>
        {
            if (stillWanted())
            {
                MoveTo(target);
            }
        });
    }
}
