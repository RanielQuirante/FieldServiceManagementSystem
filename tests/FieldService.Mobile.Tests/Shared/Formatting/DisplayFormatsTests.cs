using System.Text.RegularExpressions;
using FieldService.Mobile.Shared.Formatting;

namespace FieldService.Mobile.Tests.Shared.Formatting;

public sealed partial class DisplayFormatsTests
{
    private static readonly TimeZoneInfo _zone = TimeZoneInfo.Utc;
    private static readonly DateOnly _today = new(2026, 10, 19);

    [Fact]
    public void ATimeJoinsItsDigitsAndTheMeridiemWithANoBreakSpace()
    {
        var time = DisplayFormats.Time(At(10, 19, 14, 24), _zone);

        Assert.Equal("2:24 PM", time);
        Assert.Equal(' ', DisplayFormats.NoBreakSpace);
        Assert.DoesNotContain(' ', time);
    }

    [Fact]
    public void AShortDateJoinsItsWeekdayDayAndMonthWithNoBreakSpaces()
    {
        var date = DisplayFormats.ShortDate(new DateOnly(2026, 10, 21), _today);
        var nextYear = DisplayFormats.ShortDate(new DateOnly(2027, 10, 21), _today);

        Assert.Equal("Wed 21 Oct", date);
        Assert.Equal("Thu 21 Oct 2027", nextYear);
        Assert.DoesNotContain(' ', date);
        Assert.DoesNotContain(' ', nextYear);
    }

    [Fact]
    public void TheOfflineTimeCannotBreakBeforeItsMeridiem()
    {
        var line = "Showing this job as of " + DisplayFormats.Time(At(10, 19, 14, 24), _zone) + ".";

        Assert.Equal("Showing this job as of 2:24 PM.", line);
        Assert.DoesNotMatch(BrokenTime(), line);
    }

    [Fact]
    public void EveryComposedTimeAndDateKeepsItsPartsTogether()
    {
        var start = At(10, 21, 9, 0);
        var end = At(10, 21, 11, 0);
        var overnight = At(10, 22, 1, 30);
        var lines = new[]
        {
            DisplayFormats.TimeWindow(start, end, _zone, _today),
            DisplayFormats.TimeWindow(At(10, 21, 11, 0), At(10, 21, 13, 0), _zone, _today),
            DisplayFormats.TimeWindow(At(10, 21, 22, 0), overnight, _zone, _today),
            DisplayFormats.SpokenTimeWindow(start, end, _zone, _today),
            DisplayFormats.Scheduled(start, end, _zone, _today),
            DisplayFormats.ScheduleLine(start, end, _zone, _today),
            DisplayFormats.ScheduleLine(At(10, 19, 9, 0), At(10, 19, 11, 0), _zone, _today),
            DisplayFormats.Moment(At(10, 16, 9, 20), _zone, _today),
            DisplayFormats.AtMoment(At(10, 16, 9, 20), _zone, _today),
            DisplayFormats.AtMoment(At(10, 19, 9, 20), _zone, _today),
            DisplayFormats.DayGroupLabel(new DateOnly(2026, 10, 20), _today),
            DisplayFormats.RelativeDay(new DateOnly(2026, 10, 23), _today),
            DisplayFormats.StartsLine(At(10, 19, 9, 0), At(10, 19, 10, 0), _zone)!,
        };

        Assert.Equal("Wed 21 Oct, 9:00–11:00 AM", lines[4]);
        Assert.Equal("on Fri 16 Oct at 9:20 AM", lines[8]);
        Assert.Equal("Tomorrow · Tue 20 Oct", lines[10]);
        Assert.All(lines, line =>
        {
            Assert.DoesNotMatch(BrokenTime(), line);
            Assert.DoesNotMatch(BrokenDate(), line);
        });
    }

    private static DateTimeOffset At(int month, int day, int hour, int minute) => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    [GeneratedRegex(@"\d[ \t](AM|PM)", RegexOptions.CultureInvariant)]
    private static partial Regex BrokenTime();

    [GeneratedRegex(@"\b(Mon|Tue|Wed|Thu|Fri|Sat|Sun) \d|\d (Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\b", RegexOptions.CultureInvariant)]
    private static partial Regex BrokenDate();
}
