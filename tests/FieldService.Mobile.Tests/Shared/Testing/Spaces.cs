namespace FieldService.Mobile.Tests.Shared.Testing;

public static class Spaces
{
    public const char NoBreak = (char)0x00A0;
    public const char ZeroWidth = (char)0x200B;
    public const char MiddleDot = (char)0x00B7;

    public static string Text(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        return pattern.Replace('_', NoBreak).Replace('|', ZeroWidth);
    }
}
