namespace FieldService.Api.Persistence.Seeding;

public sealed record DemoDataCounts(
    int Users,
    int Technicians,
    int Jobs,
    int HistoryEntries,
    int Inspections,
    int Answers,
    int Readings);
