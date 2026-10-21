namespace FieldService.Api.Persistence.Seeding;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public const int MinimumDemoPasswordLength = 12;

    public bool DemoData { get; set; }

    public string? DemoPassword { get; set; }
}
