using System.Globalization;
using System.Text.RegularExpressions;

namespace FieldService.Mobile.Features.Inspection;

public enum ReadingProblem
{
    None,
    Empty,
    NotANumber,
    TooManyDecimals,
    OutOfRange,
}

public readonly record struct ReadingParse(decimal? Value, ReadingProblem Problem)
{
    public bool IsValid => Problem == ReadingProblem.None && Value is not null;
}

public static partial class ReadingValue
{
    public const int MaxLength = 12;
    public const int MaxDecimals = 2;
    public const decimal Minimum = -99999999.99m;
    public const decimal Maximum = 99999999.99m;
    public const string Placeholder = "Enter reading";
    public const string EmptyOnPassMessage = "Enter a reading to pass this item.";
    public const string NotANumberMessage = "Enter a number, such as 54 or 54.5.";
    public const string TooManyDecimalsMessage = "Use at most two decimal places.";
    public const string OutOfRangeMessage = "Enter a value between −99,999,999.99 and 99,999,999.99.";
    public const char Minus = '−';
    public const char NoBreakSpace = ' ';

    public static ReadingParse Parse(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return new ReadingParse(null, ReadingProblem.Empty);
        }

        if (!Number().IsMatch(trimmed))
        {
            return new ReadingParse(null, ReadingProblem.NotANumber);
        }

        var negative = trimmed[0] is '-' or Minus;
        var digits = (negative ? trimmed[1..] : trimmed).Replace(',', '.');
        var separator = digits.IndexOf('.', StringComparison.Ordinal);
        var whole = separator < 0 ? digits : digits[..separator];
        var fraction = separator < 0 ? string.Empty : digits[(separator + 1)..].TrimEnd('0');
        if (fraction.Length > MaxDecimals)
        {
            return new ReadingParse(null, ReadingProblem.TooManyDecimals);
        }

        var normalized = (whole.Length == 0 ? "0" : whole) + (fraction.Length == 0 ? string.Empty : "." + fraction);
        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return new ReadingParse(null, ReadingProblem.OutOfRange);
        }

        value = negative ? -value : value;
        return value is < Minimum or > Maximum
            ? new ReadingParse(null, ReadingProblem.OutOfRange)
            : new ReadingParse(value, ReadingProblem.None);
    }

    public static string? Message(ReadingProblem problem, bool requiredForPass) => problem switch
    {
        ReadingProblem.Empty => requiredForPass ? EmptyOnPassMessage : null,
        ReadingProblem.NotANumber => NotANumberMessage,
        ReadingProblem.TooManyDecimals => TooManyDecimalsMessage,
        ReadingProblem.OutOfRange => OutOfRangeMessage,
        _ => null,
    };

    public static string Format(decimal value)
    {
        var text = Math.Abs(value).ToString("0.##", CultureInfo.InvariantCulture);
        return value < 0 && text != "0" ? Minus + text : text;
    }

    public static string WithUnit(decimal value, string? unit) =>
        string.IsNullOrWhiteSpace(unit) ? Format(value) : Format(value) + NoBreakSpace + unit.Trim();

    [GeneratedRegex(@"^[-−]?([0-9]+([.,][0-9]*)?|[.,][0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex Number();
}
