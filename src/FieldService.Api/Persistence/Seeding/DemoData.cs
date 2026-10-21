using FieldService.Api.Domain;

namespace FieldService.Api.Persistence.Seeding;

public static class DemoData
{
    public const int GeneralElectricalInspection = 1;

    public const int RooftopHvacService = 2;

    public const int BoilerService = 3;

    public const int FireExtinguisherInspection = 4;

    public const int ElectricalPanelInspection = 5;

    public static DemoTime UsersCreatedAt { get; } = At(-30, 9, 0);

    public static IReadOnlyList<DemoUser> Users { get; } =
    [
        new(DemoPerson.DanaWhitfield, "Dana Whitfield", "dana.whitfield@fieldservice.example", UserRole.Administrator, Trade: null, PhoneNumber: null, IsOnShift: false),
        new(DemoPerson.MarcoReyes, "Marco Reyes", "marco.reyes@fieldservice.example", UserRole.Technician, Trade.Hvac, "(555) 014-2231", IsOnShift: true),
        new(DemoPerson.AishaKarim, "Aisha Karim", "aisha.karim@fieldservice.example", UserRole.Technician, Trade.Electrical, "(555) 014-7810", IsOnShift: true),
        new(DemoPerson.TomBecker, "Tom Becker", "tom.becker@fieldservice.example", UserRole.Technician, Trade.Plumbing, "(555) 014-3392", IsOnShift: true),
        new(DemoPerson.LenaPark, "Lena Park", "lena.park@fieldservice.example", UserRole.Technician, Trade.FireSafety, "(555) 014-5521", IsOnShift: true),
        new(DemoPerson.PriyaNatarajan, "Priya Natarajan", "priya.natarajan@fieldservice.example", UserRole.Technician, Trade.Electrical, "(555) 014-6604", IsOnShift: true),
        new(DemoPerson.SamuelOsei, "Samuel Osei", "samuel.osei@fieldservice.example", UserRole.Technician, Trade.Hvac, "(555) 014-9148", IsOnShift: false),
    ];

    public static IReadOnlyList<DemoJob> Jobs { get; } =
    [
        new(
            1036,
            "Office AC filter replacement",
            "Replace the filters on the office split units on Floor 4.",
            "Summit Coworking",
            "12 Summit Plaza, Floor 4",
            JobPriority.Low,
            RooftopHvacService,
            At(-3, 14, 0),
            At(-3, 15, 0),
            [
                new DemoStep.Created(At(-4, 9, 40)),
                new DemoStep.Assigned(At(-4, 9, 45), DemoPerson.MarcoReyes),
                new DemoStep.Cancelled(At(-3, 9, 20), "Building management replaced the filters under its own maintenance contract."),
            ]),
        new(
            1037,
            "Annual boiler service",
            "Annual service of the central heating boiler before the heating season.",
            "Harborview Apartments",
            "600 Bayfront Avenue",
            JobPriority.Normal,
            BoilerService,
            At(-3, 13, 0),
            At(-3, 15, 0),
            [
                new DemoStep.Created(At(-4, 10, 15)),
                new DemoStep.Assigned(At(-4, 10, 20), DemoPerson.TomBecker),
                new DemoStep.Started(At(-3, 13, 5)),
                new DemoStep.PutOnHold(At(-3, 13, 50), "Waiting for the gas utility to restore supply to the boiler room."),
            ]),
        new(
            1038,
            "Hot water pressure relief valve dripping",
            "The pressure relief valve on the laundry room water heater drips continuously.",
            "St. Anne's Care Home",
            "75 Willow Lane",
            JobPriority.High,
            BoilerService,
            At(-3, 15, 30),
            At(-3, 17, 0),
            [
                new DemoStep.Created(At(-3, 11, 30)),
                new DemoStep.Assigned(At(-3, 11, 35), DemoPerson.TomBecker),
                new DemoStep.Started(At(-3, 15, 40)),
                new DemoStep.PutOnHold(At(-3, 16, 25), "Waiting for the care home to approve a hot water shutdown."),
            ]),
        new(
            1039,
            "Boiler pressure drop",
            "Boiler pressure falls overnight and the heating cuts out before the practice opens.",
            "Lakeside Dental",
            "9 Lakeside Court",
            JobPriority.High,
            BoilerService,
            At(0, 7, 0),
            At(0, 8, 30),
            [
                new DemoStep.Created(At(0, 6, 58)),
                new DemoStep.Assigned(At(0, 7, 0), DemoPerson.TomBecker),
                new DemoStep.Started(At(0, 7, 5)),
                new DemoStep.InspectionSaved(
                    At(0, 7, 48),
                    [
                        Pass(1),
                        Pass(2, "Tightened a weeping fitting on the return line"),
                        Pass(3, readings: [new(1, 18.00m)]),
                        Pass(4),
                    ]),
                new DemoStep.InspectionSaved(
                    At(0, 8, 49),
                    [
                        Pass(5, "Recharged to 12 psi"),
                        Pass(6),
                        NotApplicable(7, "Not fitted on this model"),
                        Pass(8),
                    ]),
                new DemoStep.Completed(
                    At(0, 8, 52),
                    "Pressure drop traced to a weeping fitting on the return line. Fitting tightened, system refilled to 18 psi and holding."),
            ]),
        new(
            1040,
            "Dock leveler outlet not working",
            "The power outlet for the dock leveler at Dock 4 has no power. The breaker has not tripped.",
            "Northgate Logistics Warehouse",
            "42 Industrial Parkway, Dock 4",
            JobPriority.Normal,
            GeneralElectricalInspection,
            At(1, 13, 0),
            At(1, 14, 30),
            [
                new DemoStep.Created(At(-3, 14, 20)),
                new DemoStep.Assigned(At(-3, 14, 25), DemoPerson.AishaKarim),
            ]),
        new(
            1041,
            "Kitchen outlets tripping the GFCI",
            "The GFCI outlets behind the prep counter trip when the mixers start. Work must be finished before the bakery opens.",
            "Elm Street Bakery",
            "88 Elm Street",
            JobPriority.High,
            GeneralElectricalInspection,
            At(2, 7, 0),
            At(2, 8, 30),
            [
                new DemoStep.Created(At(-2, 9, 30)),
                new DemoStep.Assigned(At(-2, 9, 35), DemoPerson.AishaKarim),
            ]),
        new(
            1042,
            "Rooftop HVAC unit not cooling",
            "Rooftop unit RTU-2 runs but is not cooling. The waiting room is at 81°F. The customer reports the problem started on Saturday afternoon.",
            "Brightwater Medical Clinic",
            "214 Harbor Road, Suite 3",
            JobPriority.High,
            RooftopHvacService,
            At(0, 9, 0),
            At(0, 11, 0),
            [
                new DemoStep.Created(At(0, 8, 14)),
                new DemoStep.Assigned(At(0, 8, 20), DemoPerson.MarcoReyes),
                new DemoStep.Started(At(0, 9, 12)),
                new DemoStep.InspectionSaved(
                    At(0, 9, 31),
                    [
                        Pass(1),
                        Pass(2),
                        Pass(3, readings: [new(1, 118.00m), new(2, 285.00m)]),
                    ]),
                new DemoStep.InspectionSaved(
                    At(0, 9, 50),
                    [
                        Pass(4),
                        Fail(5, "Bearing noise at start-up. Recommend motor replacement within 30 days."),
                        NotApplicable(6, "Direct-drive unit"),
                    ]),
            ])
        {
            SiteAccessNotes = "Roof access through the east stairwell. Ask reception for the roof key.",
            ContactName = "Nina Alvarez",
            ContactPhone = "(555) 014-6620",
            Equipment = "RTU-2, 10-ton packaged rooftop unit",
        },
        new(
            1043,
            "Parking lot lighting repair",
            "Several light poles in the north parking lot stay dark after sunset.",
            "Riverside Shopping Center",
            "500 Riverside Boulevard",
            JobPriority.Normal,
            GeneralElectricalInspection,
            At(0, 7, 30),
            At(0, 9, 30),
            [
                new DemoStep.Created(At(-2, 10, 5)),
                new DemoStep.Assigned(At(-2, 10, 10), DemoPerson.AishaKarim),
                new DemoStep.Started(At(0, 7, 34)),
                new DemoStep.InspectionSaved(
                    At(0, 8, 20),
                    [
                        Pass(1, readings: [new(1, 277.00m)]),
                        Pass(2),
                        Pass(3, "Replaced corroded connectors in the pole bases"),
                        Pass(4, readings: [new(1, 250.00m)]),
                    ]),
                new DemoStep.InspectionSaved(
                    At(0, 9, 55),
                    [
                        Pass(5, readings: [new(1, 0.40m)]),
                        Pass(6),
                        NotApplicable(7, "No circuit changes were made"),
                        Pass(8),
                    ]),
                new DemoStep.Completed(
                    At(0, 9, 58),
                    "Replaced the failed lamp drivers and the corroded pole-base connectors. Every pole tested on the photocell and all lot lights are working."),
            ]),
        new(
            1044,
            "Water heater leaking at base",
            "Water is pooling around the base of the gas water heater in the utility closet.",
            "Maple Grove Townhomes",
            "19 Maple Grove Court, Unit 12",
            JobPriority.High,
            BoilerService,
            At(0, 8, 0),
            At(0, 10, 0),
            [
                new DemoStep.Created(At(-1, 11, 20)),
                new DemoStep.Assigned(At(-1, 11, 25), DemoPerson.TomBecker),
                new DemoStep.Started(At(0, 8, 6)),
                new DemoStep.PutOnHold(At(0, 10, 31), "Waiting for replacement gas valve"),
            ]),
        new(
            1045,
            "Quarterly fire extinguisher inspection",
            "Quarterly inspection of the portable fire extinguishers in the main building and the gym.",
            "Cedar Ridge Elementary School",
            "1180 Cedar Ridge Drive",
            JobPriority.Normal,
            FireExtinguisherInspection,
            At(0, 11, 30),
            At(0, 13, 0),
            [
                new DemoStep.Created(At(-1, 11, 40)),
                new DemoStep.Assigned(At(0, 8, 40), DemoPerson.LenaPark),
            ]),
        new(
            1046,
            "Exam room lights flickering",
            "The ceiling lights in the exam rooms flicker after they have been on for a few minutes.",
            "Brightwater Medical Clinic",
            "214 Harbor Road, Suite 3",
            JobPriority.Normal,
            GeneralElectricalInspection,
            At(1, 9, 0),
            At(1, 10, 30),
            [
                new DemoStep.Created(At(-1, 12, 10)),
                new DemoStep.Assigned(At(-1, 12, 15), DemoPerson.PriyaNatarajan),
            ]),
        new(
            1047,
            "Walk-in freezer temperature alarm",
            "The walk-in freezer alarm keeps sounding and the display reads above the set point. Staff moved the stock to the reach-in freezers.",
            "Elm Street Bakery",
            "88 Elm Street",
            JobPriority.Urgent,
            RooftopHvacService,
            At(0, 13, 30),
            At(0, 15, 0),
            [
                new DemoStep.Created(At(0, 8, 47)),
                new DemoStep.Assigned(At(0, 10, 5), DemoPerson.MarcoReyes),
            ])
        {
            ContactPhone = "(555) 014-4417",
        },
        new(
            1048,
            "Backflow preventer annual test",
            "Annual test of the backflow preventer on the domestic water supply, with the test report for the water utility.",
            "Harborview Apartments",
            "600 Bayfront Avenue",
            JobPriority.Normal,
            BoilerService,
            At(1, 8, 0),
            At(1, 9, 30),
            [
                new DemoStep.Created(At(-1, 16, 30)),
                new DemoStep.Assigned(At(-1, 16, 40), DemoPerson.TomBecker),
            ]),
        new(
            1049,
            "Standby generator load test",
            "Monthly load test of the standby generator with the care home running on generator power.",
            "St. Anne's Care Home",
            "75 Willow Lane",
            JobPriority.Normal,
            GeneralElectricalInspection,
            At(0, 10, 0),
            At(0, 12, 0),
            [
                new DemoStep.Created(At(-1, 13, 20)),
                new DemoStep.Assigned(At(-1, 13, 25), DemoPerson.PriyaNatarajan),
                new DemoStep.Started(At(0, 10, 42)),
            ]),
        new(
            1050,
            "Emergency lighting test",
            "Annual 90-minute discharge test of the emergency lighting across the warehouse.",
            "Northgate Logistics Warehouse",
            "42 Industrial Parkway, Dock 4",
            JobPriority.Low,
            GeneralElectricalInspection,
            At(2, 9, 0),
            At(2, 11, 0),
            [
                new DemoStep.Created(At(-1, 15, 55)),
            ]),
        new(
            1051,
            "Replace faulty breaker panel",
            "Main breaker panel trips under load. Several 20 A breakers are warm to the touch. The library closes to the public at 5 PM.",
            "Pinecrest Public Library",
            "300 Pinecrest Road",
            JobPriority.High,
            ElectricalPanelInspection,
            At(0, 15, 30),
            At(0, 17, 0),
            [
                new DemoStep.Created(At(0, 10, 20)),
            ])
        {
            SiteAccessNotes = "Main electrical room, basement level. Building supervisor will meet you at the loading door.",
            ContactName = "Owen Mitchell",
            ContactPhone = "(555) 014-3307",
        },
        new(
            1052,
            "Fire alarm panel trouble signal",
            "The fire alarm control panel shows a trouble signal on one zone. The school is on fire watch until it is cleared.",
            "Cedar Ridge Elementary School",
            "1180 Cedar Ridge Drive",
            JobPriority.Urgent,
            FireExtinguisherInspection,
            At(0, 14, 0),
            At(0, 15, 30),
            [
                new DemoStep.Created(At(0, 8, 31)),
            ]),
        new(
            1053,
            "Rooftop unit preventive maintenance",
            "Seasonal preventive maintenance on the rooftop units before the heating season.",
            "Harborview Apartments",
            "600 Bayfront Avenue",
            JobPriority.Low,
            RooftopHvacService,
            At(1, 13, 0),
            At(1, 15, 0),
            [
                new DemoStep.Created(At(-1, 16, 5)),
                new DemoStep.Assigned(At(-1, 16, 20), DemoPerson.MarcoReyes),
            ]),
        new(
            1054,
            "Leaking rooftop drain",
            "Water drips from the top-floor hallway ceiling below the rooftop drain after rain.",
            "Harborview Apartments",
            "600 Bayfront Avenue",
            JobPriority.Normal,
            BoilerService,
            At(3, 10, 0),
            At(3, 12, 0),
            [
                new DemoStep.Created(At(-1, 11, 5)),
            ]),
        new(
            1055,
            "Tenant fit-out electrical inspection",
            "Inspection of the new office circuits on Floor 4 before the tenant moves in.",
            "Summit Coworking",
            "12 Summit Plaza, Floor 4",
            JobPriority.Normal,
            GeneralElectricalInspection,
            At(0, 13, 0),
            At(0, 14, 30),
            [
                new DemoStep.Created(At(-1, 10, 40)),
                new DemoStep.Assigned(At(-1, 10, 45), DemoPerson.AishaKarim),
            ]),
    ];

    private static DemoTime At(int dayOffset, int hour, int minute) => new(dayOffset, hour, minute);

    private static DemoAnswer Pass(int item, string? note = null, IReadOnlyList<DemoReading>? readings = null) =>
        new(item, InspectionResult.Pass, note, readings ?? []);

    private static DemoAnswer Fail(int item, string note) => new(item, InspectionResult.Fail, note, []);

    private static DemoAnswer NotApplicable(int item, string note) => new(item, InspectionResult.NotApplicable, note, []);
}
