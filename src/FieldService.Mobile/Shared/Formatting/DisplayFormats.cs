using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using FieldService.Mobile.Features.Jobs;

namespace FieldService.Mobile.Shared.Formatting;

public static class DisplayFormats
{
    public const string EnDash = "–";
    public const string Separator = "\U000000A0\U000000B7 ";
    public const string ZeroWidthSpace = "\U0000200B";
    public const char NoBreakSpace = '\U000000A0';

    private static readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    public static DateTimeOffset ToLocal(DateTimeOffset instant, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return TimeZoneInfo.ConvertTime(instant, zone);
    }

    public static DateOnly LocalDay(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(ToLocal(instant, zone).DateTime);

    public static string Time(DateTimeOffset instant, TimeZoneInfo zone)
    {
        var local = ToLocal(instant, zone);
        return local.ToString("h:mm", _culture) + NoBreakSpace + local.ToString("tt", _culture);
    }

    public static string ShortDate(DateOnly day, DateOnly today)
    {
        var label = string.Join(NoBreakSpace, day.ToString("ddd", _culture), day.ToString("%d", _culture), day.ToString("MMM", _culture));
        return day.Year == today.Year ? label : string.Create(_culture, $"{label}{NoBreakSpace}{day.Year}");
    }

    public static string HeaderDate(DateOnly day) => day.ToString("dddd d MMMM", _culture);

    public static string RelativeDay(DateOnly day, DateOnly today) => (day.DayNumber - today.DayNumber) switch
    {
        0 => "Today",
        1 => "Tomorrow",
        -1 => "Yesterday",
        _ => ShortDate(day, today),
    };

    public static string DayGroupLabel(DateOnly day, DateOnly today) => (day.DayNumber - today.DayNumber) switch
    {
        0 or 1 or -1 => RelativeDay(day, today) + Separator + ShortDate(day, today),
        _ => ShortDate(day, today),
    };

    public static string TimeWindow(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone, DateOnly today)
    {
        var from = ToLocal(start, zone);
        var to = ToLocal(end, zone);
        if (from.Date != to.Date)
        {
            return Time(start, zone) + EnDash + ShortDate(DateOnly.FromDateTime(to.DateTime), today) + ", " + Time(end, zone);
        }

        var sameMeridiem = (from.Hour < 12) == (to.Hour < 12);
        return sameMeridiem
            ? from.ToString("h:mm", _culture) + EnDash + Time(end, zone)
            : Time(start, zone) + EnDash + Time(end, zone);
    }

    [return: NotNullIfNotNull(nameof(text))]
    public static string? BreakAfterEnDash(string? text) => text?.Replace(EnDash, EnDash + ZeroWidthSpace, StringComparison.Ordinal);

    public static string SpokenTimeWindow(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone, DateOnly today) =>
        TimeWindow(start, end, zone, today).Replace(EnDash, " to ", StringComparison.Ordinal);

    public static string Scheduled(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone, DateOnly today) =>
        ShortDate(LocalDay(start, zone), today) + ", " + TimeWindow(start, end, zone, today);

    public static string ScheduleLine(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone, DateOnly today)
    {
        var day = LocalDay(start, zone);
        return (day.DayNumber - today.DayNumber) switch
        {
            0 or 1 => RelativeDay(day, today) + " " + TimeWindow(start, end, zone, today),
            _ => Scheduled(start, end, zone, today),
        };
    }

    public static string Moment(DateTimeOffset instant, TimeZoneInfo zone, DateOnly today)
    {
        var day = LocalDay(instant, zone);
        return day == today ? Time(instant, zone) : ShortDate(day, today) + ", " + Time(instant, zone);
    }

    public static string AtMoment(DateTimeOffset instant, TimeZoneInfo zone, DateOnly today)
    {
        var day = LocalDay(instant, zone);
        return day == today
            ? "at " + Time(instant, zone)
            : "on " + ShortDate(day, today) + " at " + Time(instant, zone);
    }

    public static string Duration(TimeSpan duration)
    {
        var totalMinutes = Math.Max(0, (long)Math.Floor(duration.TotalMinutes));
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        if (hours == 0)
        {
            return string.Create(_culture, $"{minutes}{NoBreakSpace}min");
        }

        return minutes == 0
            ? string.Create(_culture, $"{hours}{NoBreakSpace}h")
            : string.Create(_culture, $"{hours}{NoBreakSpace}h{NoBreakSpace}{minutes}{NoBreakSpace}min");
    }

    public static string? StartsLine(DateTimeOffset start, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (LocalDay(start, zone) != LocalDay(now, zone))
        {
            return null;
        }

        var remaining = start - now;
        if (remaining < TimeSpan.Zero)
        {
            return "Was due to start at " + Time(start, zone);
        }

        return remaining < TimeSpan.FromMinutes(1) ? "Starts now" : "Starts in " + Duration(remaining);
    }

    public static string JobNumber(long number) => string.Create(_culture, $"JOB-{number}");

    public static string Initials(string? displayName)
    {
        var words = (displayName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length switch
        {
            0 => string.Empty,
            1 => char.ToUpperInvariant(words[0][0]).ToString(),
            _ => string.Concat(char.ToUpperInvariant(words[0][0]), char.ToUpperInvariant(words[^1][0])),
        };
    }

    public static string StatusLabel(JobStatus status) => status switch
    {
        JobStatus.Unassigned => "Unassigned",
        JobStatus.Assigned => "Assigned",
        JobStatus.InProgress => "In progress",
        JobStatus.OnHold => "On hold",
        JobStatus.Completed => "Completed",
        JobStatus.Cancelled => "Cancelled",
        _ => "Unknown",
    };

    public static string PriorityLabel(JobPriority priority) => priority switch
    {
        JobPriority.Low => "Low",
        JobPriority.Normal => "Normal",
        JobPriority.High => "High",
        JobPriority.Urgent => "Urgent",
        _ => "Unknown",
    };
}
