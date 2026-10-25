namespace FieldService.Mobile.Shared.Controls;

public static class FocusMoves
{
    public const string RevealCommand = "FsRevealForFocus";

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
