using FieldService.Mobile.Features.Inspection;

namespace FieldService.Mobile.Tests.Features.Inspection;

public sealed class ReadingValueTests
{
    [Theory]
    [InlineData("54", "54")]
    [InlineData("54.5", "54.5")]
    [InlineData("54,5", "54.5")]
    [InlineData("54.50", "54.5")]
    [InlineData("54.500", "54.5")]
    [InlineData("-3.25", "-3.25")]
    [InlineData("−3.25", "-3.25")]
    [InlineData(".5", "0.5")]
    [InlineData("12.", "12")]
    [InlineData(" 118 ", "118")]
    [InlineData("-99999999.99", "-99999999.99")]
    [InlineData("99999999.99", "99999999.99")]
    public void AValidReadingTakesADotOrACommaAndALeadingMinus(string text, string expected)
    {
        var parsed = ReadingValue.Parse(text);

        Assert.True(parsed.IsValid);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), parsed.Value);
    }

    [Theory]
    [InlineData("", ReadingProblem.Empty)]
    [InlineData("   ", ReadingProblem.Empty)]
    [InlineData("abc", ReadingProblem.NotANumber)]
    [InlineData("1,000.5", ReadingProblem.NotANumber)]
    [InlineData("5e3", ReadingProblem.NotANumber)]
    [InlineData("+5", ReadingProblem.NotANumber)]
    [InlineData("--5", ReadingProblem.NotANumber)]
    [InlineData("٥٤", ReadingProblem.NotANumber)]
    [InlineData("54.555", ReadingProblem.TooManyDecimals)]
    [InlineData("100000000", ReadingProblem.OutOfRange)]
    [InlineData("-100000000", ReadingProblem.OutOfRange)]
    [InlineData("99999999.999", ReadingProblem.TooManyDecimals)]
    public void AnInvalidReadingNamesItsProblem(string text, ReadingProblem problem)
    {
        var parsed = ReadingValue.Parse(text);

        Assert.False(parsed.IsValid);
        Assert.Equal(problem, parsed.Problem);
    }

    [Fact]
    public void EveryProblemHasTheSpecMessageAndAnEmptyReadingIsAnErrorOnlyForAPass()
    {
        Assert.Equal("Enter a reading to pass this item.", ReadingValue.Message(ReadingProblem.Empty, requiredForPass: true));
        Assert.Null(ReadingValue.Message(ReadingProblem.Empty, requiredForPass: false));
        Assert.Equal("Enter a number, such as 54 or 54.5.", ReadingValue.Message(ReadingProblem.NotANumber, false));
        Assert.Equal("Use at most two decimal places.", ReadingValue.Message(ReadingProblem.TooManyDecimals, false));
        Assert.Equal("Enter a value between −99,999,999.99 and 99,999,999.99.", ReadingValue.Message(ReadingProblem.OutOfRange, false));
        Assert.Null(ReadingValue.Message(ReadingProblem.None, true));
        Assert.Equal(ReadingValue.MaxLength, "−99999999.99".Length);
    }

    [Theory]
    [InlineData("118.00", "118")]
    [InlineData("0.40", "0.4")]
    [InlineData("54", "54")]
    [InlineData("-3.50", "−3.5")]
    [InlineData("1234567.89", "1234567.89")]
    [InlineData("0", "0")]
    public void ADisplayedValueHasNoTrailingZerosNoGroupingAndATrueMinus(string value, string expected)
    {
        Assert.Equal(expected, ReadingValue.Format(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void AValueAndItsUnitAreJoinedByANoBreakSpace()
    {
        Assert.Equal("54 °F", ReadingValue.WithUnit(54.00m, "°F"));
        Assert.Equal("0.4 Ω", ReadingValue.WithUnit(0.40m, "Ω"));
        Assert.Equal("118", ReadingValue.WithUnit(118m, " "));
    }

    [Fact]
    public void ASavedValueShownInAFieldParsesBackToItself()
    {
        foreach (var value in new[] { 118.00m, -3.5m, 0.4m, 99999999.99m, -99999999.99m })
        {
            Assert.Equal(value, ReadingValue.Parse(ReadingValue.Format(value)).Value);
        }
    }
}
