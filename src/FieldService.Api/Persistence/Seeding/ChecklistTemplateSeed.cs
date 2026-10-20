using FieldService.Api.Domain;

namespace FieldService.Api.Persistence.Seeding;

public static class ChecklistTemplateSeed
{
    private const string IdPrefix = "01a15175-3c00-7000-8000-";

    private static readonly TemplateRow[] _rows =
    [
        new(1, "General electrical inspection", Trade.Electrical,
        [
            new("Measure supply voltage", [new("Supply voltage", "V")]),
            new("Isolate and lock out the circuits being worked on", []),
            new("Inspect wiring, terminations and enclosures", []),
            new("Test insulation resistance", [new("Insulation resistance", "MΩ")]),
            new("Test ground continuity", [new("Ground resistance", "Ω")]),
            new("Check fixtures, switches and receptacles", []),
            new("Label circuits and update the panel directory", []),
            new("Restore power and confirm normal operation", []),
        ]),
        new(2, "Rooftop HVAC service", Trade.Hvac,
        [
            new("Lock out and tag out unit power", []),
            new("Replace return air filters", []),
            new("Check refrigerant pressures", [new("Suction", "psi"), new("Discharge", "psi")]),
            new("Clean condenser coil", []),
            new("Inspect condenser fan motor and bearings", []),
            new("Check belts and pulleys", []),
            new("Clear condensate drain line", []),
            new("Record supply air temperature", [new("Supply air temperature", "°F")]),
            new("Test thermostat and economizer controls", []),
            new("Restore power and confirm normal operation", []),
        ]),
        new(3, "Boiler service", Trade.Plumbing,
        [
            new("Isolate gas and electrical supply", []),
            new("Inspect valves and fittings for leaks", []),
            new("Check system pressure", [new("System pressure", "psi")]),
            new("Test pressure relief valve", []),
            new("Inspect expansion tank", []),
            new("Check burner flame and combustion", []),
            new("Test low-water cutoff", []),
            new("Restore supply and confirm operation", []),
        ]),
        new(4, "Fire extinguisher inspection", Trade.FireSafety,
        [
            new("Confirm each extinguisher is in its marked location", []),
            new("Check access is clear and signage is visible", []),
            new("Check the pressure gauge reads in the operable range", []),
            new("Inspect the pin, tamper seal and hose", []),
            new("Check for dents, corrosion or leakage", []),
            new("Weigh CO2 extinguishers", [new("Gross weight", "lb")]),
            new("Sign and date the inspection tag", []),
        ]),
        new(5, "Electrical panel inspection", Trade.Electrical,
        [
            new("Measure line voltage", [new("Line to line", "V"), new("Line to neutral", "V")]),
            new("Measure load current", [new("Load current", "A")]),
            new("Check breaker temperatures under load", [new("Hottest breaker", "°F")]),
            new("Isolate the panel and verify it is de-energized", []),
            new("Inspect the enclosure, cover and circuit directory", []),
            new("Check breakers for heat damage and correct ratings", []),
            new("Torque-check lugs and terminations", []),
            new("Restore power and confirm normal operation", []),
        ]),
    ];

    public static IReadOnlyList<ChecklistTemplate> Templates { get; } =
    [
        .. _rows.Select(template => ChecklistTemplate.Create(
            TemplateId(template.Number),
            template.Name,
            template.Trade)),
    ];

    public static IReadOnlyList<ChecklistItem> Items { get; } =
    [
        .. _rows.SelectMany(template => template.Items.Select((item, index) => ChecklistItem.Create(
            ItemId(template.Number, index + 1),
            TemplateId(template.Number),
            (short)(index + 1),
            item.Prompt))),
    ];

    public static IReadOnlyList<ChecklistReadingField> ReadingFields { get; } =
    [
        .. _rows.SelectMany(template => template.Items.SelectMany((item, itemIndex) => item.Fields.Select(
            (field, fieldIndex) => ChecklistReadingField.Create(
                ReadingFieldId(template.Number, itemIndex + 1, fieldIndex + 1),
                ItemId(template.Number, itemIndex + 1),
                (short)(fieldIndex + 1),
                field.Label,
                field.Unit)))),
    ];

    public static Guid TemplateId(int template)
    {
        return Guid.Parse($"{IdPrefix}0001{template:D2}000000");
    }

    public static Guid ItemId(int template, int item)
    {
        return Guid.Parse($"{IdPrefix}0002{template:D2}{item:D2}0000");
    }

    public static Guid ReadingFieldId(int template, int item, int field)
    {
        return Guid.Parse($"{IdPrefix}0003{template:D2}{item:D2}{field:D2}00");
    }

    private sealed record TemplateRow(int Number, string Name, Trade Trade, ItemRow[] Items);

    private sealed record ItemRow(string Prompt, FieldRow[] Fields);

    private sealed record FieldRow(string Label, string Unit);
}
