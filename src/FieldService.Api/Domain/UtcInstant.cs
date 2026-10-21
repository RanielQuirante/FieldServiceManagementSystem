namespace FieldService.Api.Domain;

public static class UtcInstant
{
    public static DateTimeOffset Truncate(DateTimeOffset value)
    {
        var ticks = value.UtcTicks;
        return new DateTimeOffset(ticks - (ticks % TimeSpan.TicksPerMillisecond), TimeSpan.Zero);
    }
}
