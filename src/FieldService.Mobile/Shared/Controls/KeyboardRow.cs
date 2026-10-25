namespace FieldService.Mobile.Shared.Controls;

public enum KeyboardRowRole
{
    RadioButton,
    CheckBox,
}

public enum KeyboardRowKey
{
    Other,
    Space,
    Enter,
}

public interface IKeyboardRow
{
    KeyboardRowRole Role { get; }

    string AccessibleName { get; }

    string? AccessibleDetail { get; }

    bool IsOn { get; }

    bool IsOperable { get; }

    double FocusCornerRadius { get; }

    Thickness FocusInset { get; }

    bool Operate();
}

public static class KeyboardRows
{
    public static bool Handles(KeyboardRowRole role, KeyboardRowKey key) => key switch
    {
        KeyboardRowKey.Space => true,
        KeyboardRowKey.Enter => role == KeyboardRowRole.RadioButton,
        _ => false,
    };

    public static bool Acts(KeyboardRowRole role, KeyboardRowKey key, bool released) => key switch
    {
        KeyboardRowKey.Space => released,
        KeyboardRowKey.Enter => role == KeyboardRowRole.RadioButton && !released,
        _ => false,
    };

    public static bool Press(IKeyboardRow row, KeyboardRowKey key, bool released)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (!Handles(row.Role, key))
        {
            return false;
        }

        if (Acts(row.Role, key, released) && row.IsOperable)
        {
            row.Operate();
        }

        return true;
    }
}
