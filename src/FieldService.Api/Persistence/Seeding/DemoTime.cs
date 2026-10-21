namespace FieldService.Api.Persistence.Seeding;

public readonly record struct DemoTime(int DayOffset, int Hour, int Minute)
{
    public override string ToString() =>
        DayOffset switch
        {
            0 => $"D {Hour:D2}:{Minute:D2}",
            > 0 => $"D+{DayOffset} {Hour:D2}:{Minute:D2}",
            _ => $"D{DayOffset} {Hour:D2}:{Minute:D2}",
        };
}
