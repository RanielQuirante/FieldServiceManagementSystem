using System.Net;
using System.Text;
using System.Text.Json;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Http;

namespace FieldService.Mobile.Tests.Shared.Testing;

public static class JobDtoBuilder
{
    public static readonly TimeSpan LocalOffset = TimeSpan.FromHours(-4);

    public static readonly DateTimeOffset Now = new(2026, 10, 19, 14, 42, 0, TimeSpan.Zero);

    public static readonly Guid Dana = Guid.Parse("01a0f6b1-0000-7000-8000-000000000001");
    public static readonly Guid Marco = Guid.Parse("01a0f6b1-0000-7000-8000-000000000002");
    public static readonly Guid Tom = Guid.Parse("01a0f6b1-0000-7000-8000-000000000003");

    public static readonly Guid Job1036 = Guid.Parse("01a0f6b1-1036-7000-8000-000000001036");
    public static readonly Guid Job1037 = Guid.Parse("01a0f6b1-1037-7000-8000-000000001037");
    public static readonly Guid Job1039 = Guid.Parse("01a0f6b1-1039-7000-8000-000000001039");
    public static readonly Guid Job1042 = Guid.Parse("01a0f6b1-1042-7000-8000-000000001042");
    public static readonly Guid Job1047 = Guid.Parse("01a0f6b1-1047-7000-8000-000000001047");
    public static readonly Guid Job1053 = Guid.Parse("01a0f6b1-1053-7000-8000-000000001053");

    private static readonly UserReference _dana = new(Dana, "Dana Whitfield");
    private static readonly UserReference _marco = new(Marco, "Marco Reyes");
    private static readonly UserReference _tom = new(Tom, "Tom Becker");

    public static TimeZoneInfo Zone { get; } =
        TimeZoneInfo.CreateCustomTimeZone("FieldService Test", LocalOffset, "FieldService Test", "FieldService Test");

    public static DateTimeOffset Local(int month, int day, int hour, int minute) =>
        new(2026, month, day, hour, minute, 0, LocalOffset);

    public static AssignedJobResponse Assigned(
        Guid id,
        long number,
        string title,
        JobPriority priority,
        JobStatus status,
        string customer,
        string site,
        DateTimeOffset start,
        DateTimeOffset end,
        DateTimeOffset? completedAt = null,
        DateTimeOffset? cancelledAt = null) =>
        new(id, number, title, priority, status, customer, site, start, end, null, completedAt, cancelledAt, completedAt ?? cancelledAt ?? Now, 7);

    public static AssignedJobResponse Job1042Summary() => Assigned(
        Job1042, 1042, "Rooftop HVAC unit not cooling", JobPriority.High, JobStatus.InProgress,
        "Brightwater Medical Clinic", "214 Harbor Road, Suite 3", Local(10, 19, 9, 0), Local(10, 19, 11, 0));

    public static AssignedJobResponse Job1047Summary() => Assigned(
        Job1047, 1047, "Walk-in freezer temperature alarm", JobPriority.Urgent, JobStatus.Assigned,
        "Elm Street Bakery", "88 Elm Street", Local(10, 19, 13, 30), Local(10, 19, 15, 0));

    public static AssignedJobResponse Job1053Summary() => Assigned(
        Job1053, 1053, "Rooftop unit preventive maintenance", JobPriority.Low, JobStatus.Assigned,
        "Harborview Apartments", "600 Bayfront Avenue", Local(10, 20, 13, 0), Local(10, 20, 15, 0));

    public static AssignedJobResponse Job1037Summary() => Assigned(
        Job1037, 1037, "Annual boiler service", JobPriority.Normal, JobStatus.OnHold,
        "Harborview Apartments", "600 Bayfront Avenue", Local(10, 16, 13, 0), Local(10, 16, 15, 0));

    public static AssignedJobResponse Job1036Summary() => Assigned(
        Job1036, 1036, "Office AC filter replacement", JobPriority.Low, JobStatus.Cancelled,
        "Summit Coworking", "12 Summit Plaza, Floor 4", Local(10, 16, 14, 0), Local(10, 16, 15, 0),
        cancelledAt: Local(10, 16, 9, 20));

    public static GetAssignedJobsResponse MarcoActive() => new([Job1042Summary(), Job1047Summary(), Job1053Summary()]);

    public static GetAssignedJobsResponse MarcoCompleted() => new([Job1036Summary()]);

    public static JobDetailsResponse Details1042() => new(
        Job1042,
        1042,
        "Rooftop HVAC unit not cooling",
        "Rooftop unit RTU-2 runs but is not cooling. The waiting room is at 81°F. The customer reports the problem started on Saturday afternoon.",
        JobPriority.High,
        JobStatus.InProgress,
        "Brightwater Medical Clinic",
        "214 Harbor Road, Suite 3",
        "Roof access through the east stairwell. Ask reception for the roof key.",
        "Nina Alvarez",
        "(555) 014-6620",
        "RTU-2, 10-ton packaged rooftop unit",
        Local(10, 19, 9, 0),
        Local(10, 19, 11, 0),
        new ChecklistTemplateReference(Guid.NewGuid(), "Rooftop HVAC service", Trade.Hvac),
        new JobTechnicianReference(Marco, "Marco Reyes", Trade.Hvac),
        _dana,
        Local(10, 19, 8, 14),
        Local(10, 19, 9, 50),
        Local(10, 19, 8, 20),
        Local(10, 19, 9, 12),
        null,
        null,
        [
            History(JobHistoryAction.Created, null, JobStatus.Unassigned, _dana, null, null, Local(10, 19, 8, 14)),
            History(JobHistoryAction.Assigned, JobStatus.Unassigned, JobStatus.Assigned, _dana, _marco, null, Local(10, 19, 8, 20)),
            History(JobHistoryAction.Started, JobStatus.Assigned, JobStatus.InProgress, _marco, null, null, Local(10, 19, 9, 12)),
        ],
        11,
        new JobAffordances(false));

    public static JobDetailsResponse Details1047() => new(
        Job1047,
        1047,
        "Walk-in freezer temperature alarm",
        "The walk-in freezer alarm keeps sounding and the display reads above the set point. Staff moved the stock to the reach-in freezers.",
        JobPriority.Urgent,
        JobStatus.Assigned,
        "Elm Street Bakery",
        "88 Elm Street",
        null,
        null,
        "(555) 014-4417",
        null,
        Local(10, 19, 13, 30),
        Local(10, 19, 15, 0),
        new ChecklistTemplateReference(Guid.NewGuid(), "Rooftop HVAC service", Trade.Hvac),
        new JobTechnicianReference(Marco, "Marco Reyes", Trade.Hvac),
        _dana,
        Local(10, 19, 8, 47),
        Local(10, 19, 10, 5),
        Local(10, 19, 10, 5),
        null,
        null,
        null,
        [
            History(JobHistoryAction.Created, null, JobStatus.Unassigned, _dana, null, null, Local(10, 19, 8, 47)),
            History(JobHistoryAction.Assigned, JobStatus.Unassigned, JobStatus.Assigned, _dana, _marco, null, Local(10, 19, 10, 5)),
        ],
        9,
        new JobAffordances(true));

    public static JobDetailsResponse Details1039() => Details1042() with
    {
        Id = Job1039,
        Number = 1039,
        Title = "Boiler pressure drop",
        Status = JobStatus.Completed,
        SiteAccessNotes = null,
        ContactName = null,
        ContactPhone = null,
        Equipment = null,
        CompletedAt = Local(10, 19, 8, 52),
        History =
        [
            History(JobHistoryAction.Created, null, JobStatus.Unassigned, _dana, null, null, Local(10, 19, 6, 58)),
            History(JobHistoryAction.Assigned, JobStatus.Unassigned, JobStatus.Assigned, _dana, _tom, null, Local(10, 19, 7, 0)),
            History(JobHistoryAction.Started, JobStatus.Assigned, JobStatus.InProgress, _tom, null, null, Local(10, 19, 7, 5)),
            History(JobHistoryAction.Completed, JobStatus.InProgress, JobStatus.Completed, _tom, null, null, Local(10, 19, 8, 52)),
        ],
    };

    public static JobDetailsResponse Details1036() => Details1042() with
    {
        Id = Job1036,
        Number = 1036,
        Title = "Office AC filter replacement",
        Status = JobStatus.Cancelled,
        CancelledAt = Local(10, 16, 9, 20),
        History =
        [
            History(JobHistoryAction.Created, null, JobStatus.Unassigned, _dana, null, null, Local(10, 15, 9, 40)),
            History(JobHistoryAction.Assigned, JobStatus.Unassigned, JobStatus.Assigned, _dana, _marco, null, Local(10, 15, 9, 45)),
            History(
                JobHistoryAction.Cancelled,
                JobStatus.Assigned,
                JobStatus.Cancelled,
                _dana,
                null,
                "Building management replaced the filters under its own maintenance contract.",
                Local(10, 16, 9, 20)),
        ],
    };

    public static JobDetailsResponse Details1037() => Details1042() with
    {
        Id = Job1037,
        Number = 1037,
        Title = "Annual boiler service",
        Status = JobStatus.OnHold,
        History =
        [
            History(JobHistoryAction.Created, null, JobStatus.Unassigned, _dana, null, null, Local(10, 15, 10, 15)),
            History(JobHistoryAction.Assigned, JobStatus.Unassigned, JobStatus.Assigned, _dana, _tom, null, Local(10, 15, 10, 20)),
            History(JobHistoryAction.Started, JobStatus.Assigned, JobStatus.InProgress, _tom, null, null, Local(10, 16, 13, 5)),
            History(
                JobHistoryAction.PutOnHold,
                JobStatus.InProgress,
                JobStatus.OnHold,
                _tom,
                null,
                "Waiting for the gas utility to restore supply to the boiler room.",
                Local(10, 16, 13, 50)),
        ],
    };

    public static JobHistoryEntryResponse History(
        JobHistoryAction action,
        JobStatus? from,
        JobStatus to,
        UserReference actor,
        UserReference? technician,
        string? note,
        DateTimeOffset occurredAt) =>
        new(Guid.NewGuid(), action, from, to, actor, technician, note, occurredAt);

    public static HttpResponseMessage Json(GetAssignedJobsResponse body) =>
        Respond(JsonSerializer.Serialize(body, MobileJsonContext.Default.GetAssignedJobsResponse));

    public static HttpResponseMessage Json(JobDetailsResponse body) =>
        Respond(JsonSerializer.Serialize(body, MobileJsonContext.Default.JobDetailsResponse));

    public static HttpResponseMessage Respond(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
