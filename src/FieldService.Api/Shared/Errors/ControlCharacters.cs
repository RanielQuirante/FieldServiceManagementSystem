namespace FieldService.Api.Shared.Errors;

public static class ControlCharacters
{
    public static bool AreAllowedIn(object? value, bool allowLineBreaks)
    {
        if (value is null)
        {
            return true;
        }

        if (value is not string text)
        {
            return false;
        }

        foreach (var character in text)
        {
            if (char.IsControl(character) && !(allowLineBreaks && (character is '\n' or '\r' or '\t')))
            {
                return false;
            }
        }

        return true;
    }
}
