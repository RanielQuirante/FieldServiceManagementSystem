namespace FieldService.Api.Persistence.Seeding;

public sealed class DemoTimeAnchor
{
    public static readonly TimeOnly DesignNow = new(10, 42);

    private DemoTimeAnchor(DateOnly day, TimeZoneInfo zone, int deltaHours)
    {
        Day = day;
        Zone = zone;
        DeltaHours = deltaHours;
    }

    public DateOnly Day { get; }

    public TimeZoneInfo Zone { get; }

    public int DeltaHours { get; }

    public static DemoTimeAnchor From(TimeProvider time)
    {
        var zone = time.LocalTimeZone;
        var day = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var designNowUtc = ToUtc(day.ToDateTime(DesignNow), zone);
        return new DemoTimeAnchor(day, zone, FloorHours(time.GetUtcNow() - designNowUtc));
    }

    public static DateTimeOffset ToUtc(DateTime localTime, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            return new DateTimeOffset(local, zone.GetUtcOffset(local.AddDays(-1))).ToUniversalTime();
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    public DateTimeOffset Resolve(DemoTime time)
    {
        var local = Day.AddDays(time.DayOffset).ToDateTime(new TimeOnly(time.Hour, time.Minute));
        return ToUtc(local, Zone).AddHours(DeltaHours);
    }

    private static int FloorHours(TimeSpan elapsed)
    {
        var hours = Math.DivRem(elapsed.Ticks, TimeSpan.TicksPerHour, out var remainder);
        return (int)(remainder < 0 ? hours - 1 : hours);
    }
}
