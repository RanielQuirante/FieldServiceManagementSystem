namespace FieldService.Api.Persistence.Seeding;

public sealed record DemoDataSeedResult(DemoDataSeedOutcome Outcome, DemoDataCounts? Counts, long? NextJobNumber)
{
    public static DemoDataSeedResult Skipped(DemoDataSeedOutcome outcome) => new(outcome, Counts: null, NextJobNumber: null);
}
