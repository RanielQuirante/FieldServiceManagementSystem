using FieldService.Api.Domain;

namespace FieldService.Api.Persistence.Seeding;

public sealed record DemoAnswer(int Item, InspectionResult Result, string? Note, IReadOnlyList<DemoReading> Readings);
