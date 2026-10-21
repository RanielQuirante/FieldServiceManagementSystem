namespace FieldService.Api.Persistence.Seeding;

public abstract record DemoStep(DemoTime At)
{
    public abstract string Name { get; }

    public sealed record Created(DemoTime At) : DemoStep(At)
    {
        public override string Name => "Created";
    }

    public sealed record Assigned(DemoTime At, DemoPerson Technician) : DemoStep(At)
    {
        public override string Name => "Assigned";
    }

    public sealed record Started(DemoTime At) : DemoStep(At)
    {
        public override string Name => "Started";
    }

    public sealed record PutOnHold(DemoTime At, string Note) : DemoStep(At)
    {
        public override string Name => "PutOnHold";
    }

    public sealed record InspectionSaved(DemoTime At, IReadOnlyList<DemoAnswer> Answers) : DemoStep(At)
    {
        public override string Name => "InspectionSaved";
    }

    public sealed record Completed(DemoTime At, string TechnicianNotes) : DemoStep(At)
    {
        public override string Name => "Completed";
    }

    public sealed record Cancelled(DemoTime At, string Reason) : DemoStep(At)
    {
        public override string Name => "Cancelled";
    }
}
