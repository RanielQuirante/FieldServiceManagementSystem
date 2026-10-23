using System.Net;
using System.Text;
using System.Text.Json;
using FieldService.Mobile.Features.Inspection;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Http;

namespace FieldService.Mobile.Tests.Shared.Testing;

public static class InspectionDtoBuilder
{
    public const string FanNote = "Bearing noise at start-up. Recommend motor replacement within 30 days.";
    public const string BeltNote = "Direct-drive unit";
    public const uint Version = 48213;

    public static readonly Guid Suction = Item(3, 1);
    public static readonly Guid Discharge = Item(3, 2);
    public static readonly Guid SupplyAir = Item(8, 1);

    private static readonly string[] _prompts =
    [
        "Lock out and tag out unit power",
        "Replace return air filters",
        "Check refrigerant pressures",
        "Clean condenser coil",
        "Inspect condenser fan motor and bearings",
        "Check belts and pulleys",
        "Clear condensate drain line",
        "Record supply air temperature",
        "Test thermostat and economizer controls",
        "Restore power and confirm normal operation",
    ];

    public static Guid ItemId(int position) => Guid.Parse($"01a15175-3c00-7000-8000-0002{position:D2}000000");

    public static InspectionResponse Draft1042(uint version = Version, bool canEdit = true, JobStatus status = JobStatus.InProgress)
    {
        var items = Enumerable.Range(1, 10).Select(position => new InspectionItemResponse(
            ItemId(position),
            position,
            _prompts[position - 1],
            Fields(position),
            position <= 6 ? SavedAnswer(position) : null)).ToList();
        return Build(items, version, canEdit, status, submittedAt: null);
    }

    public static InspectionResponse Complete1042(uint version = Version + 1, bool canEdit = true, JobStatus status = JobStatus.InProgress, DateTimeOffset? submittedAt = null)
    {
        var items = Enumerable.Range(1, 10).Select(position => new InspectionItemResponse(
            ItemId(position),
            position,
            _prompts[position - 1],
            Fields(position),
            position <= 6 ? SavedAnswer(position) : FinalAnswer(position))).ToList();
        return Build(items, version, canEdit, status, submittedAt);
    }

    public static InspectionResponse WithAnswers(InspectionResponse inspection, uint version, params (int Position, InspectionAnswerResponse? Answer)[] answers)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        var items = inspection.Items
            .Select(item => answers.FirstOrDefault(pair => pair.Position == item.Position) is { Position: > 0 } change ? item with { Answer = change.Answer } : item)
            .ToList();
        return Build(items, version, inspection.Affordances.CanEdit, inspection.JobStatus, inspection.SubmittedAt);
    }

    public static InspectionAnswerResponse Answer(InspectionResult result, string? note = null, params (Guid Field, decimal Value)[] readings) =>
        new(result, note, [.. readings.Select(reading => new InspectionReadingResponse(reading.Field, reading.Value))], JobDtoBuilder.Local(10, 19, 10, 56));

    public static HttpResponseMessage Json(InspectionResponse body) =>
        JobDtoBuilder.Respond(JsonSerializer.Serialize(body, MobileJsonContext.Default.InspectionResponse));

    public static HttpResponseMessage Locked() =>
        ProblemResponses.Problem(HttpStatusCode.Conflict, InspectionViewModel.LockedCode, "The inspection is locked.");

    public static HttpResponseMessage Conflict() =>
        ProblemResponses.Problem(HttpStatusCode.Conflict, InspectionViewModel.ConcurrencyConflictCode, "The job was changed by someone else.");

    public static HttpResponseMessage Incomplete() =>
        ProblemResponses.Problem(HttpStatusCode.Conflict, SubmitAffordance.IncompleteCode, "The inspection checklist is not complete.");

    public static HttpResponseMessage InvalidTransition() =>
        ProblemResponses.Problem(HttpStatusCode.Conflict, SubmitAffordance.InvalidTransitionCode, "The job cannot change to that status.");

    private static Guid Item(int position, int field) => Guid.Parse($"01a15175-3c00-7000-8000-0003{position:D2}{field:D2}0000");

    private static IReadOnlyList<InspectionReadingFieldResponse> Fields(int position) => position switch
    {
        3 => [new InspectionReadingFieldResponse(Suction, 1, "Suction", "psi"), new InspectionReadingFieldResponse(Discharge, 2, "Discharge", "psi")],
        8 => [new InspectionReadingFieldResponse(SupplyAir, 1, "Supply air temperature", "°F")],
        _ => [],
    };

    private static InspectionAnswerResponse SavedAnswer(int position) => position switch
    {
        3 => new InspectionAnswerResponse(InspectionResult.Pass, null, [new InspectionReadingResponse(Suction, 118.00m), new InspectionReadingResponse(Discharge, 285.00m)], JobDtoBuilder.Local(10, 19, 9, 31)),
        5 => new InspectionAnswerResponse(InspectionResult.Fail, FanNote, [], JobDtoBuilder.Local(10, 19, 9, 50)),
        6 => new InspectionAnswerResponse(InspectionResult.NotApplicable, BeltNote, [], JobDtoBuilder.Local(10, 19, 9, 50)),
        _ => new InspectionAnswerResponse(InspectionResult.Pass, null, [], JobDtoBuilder.Local(10, 19, 9, position <= 3 ? 31 : 50)),
    };

    private static InspectionAnswerResponse FinalAnswer(int position) => position == 8
        ? new InspectionAnswerResponse(InspectionResult.Pass, null, [new InspectionReadingResponse(SupplyAir, 54.00m)], JobDtoBuilder.Local(10, 19, 10, 56))
        : new InspectionAnswerResponse(InspectionResult.Pass, null, [], JobDtoBuilder.Local(10, 19, 10, 56));

    private static InspectionResponse Build(IReadOnlyList<InspectionItemResponse> items, uint version, bool canEdit, JobStatus status, DateTimeOffset? submittedAt)
    {
        var answered = items.Count(item => item.Answer is not null);
        var progress = new InspectionProgress(
            answered,
            items.Count,
            items.Count(item => item.Answer?.Result == InspectionResult.Pass),
            items.Count(item => item.Answer?.Result == InspectionResult.Fail),
            items.Count(item => item.Answer?.Result == InspectionResult.NotApplicable));
        var allowed = canEdit && answered == items.Count;
        var canSubmit = new SubmitAffordance(
            allowed,
            allowed ? null : status == JobStatus.InProgress ? SubmitAffordance.IncompleteCode : SubmitAffordance.InvalidTransitionCode,
            answered,
            items.Count);
        return new InspectionResponse(
            JobDtoBuilder.Job1042,
            1042,
            status,
            new ChecklistTemplateReference(Guid.Parse("01a15175-3c00-7000-8000-000102000000"), "Rooftop HVAC service", Trade.Hvac),
            items,
            progress,
            JobDtoBuilder.Local(10, 19, 9, 12),
            submittedAt,
            answered == 0 ? null : JobDtoBuilder.Local(10, 19, 9, 50),
            submittedAt,
            null,
            version,
            new InspectionAffordances(canEdit, canSubmit));
    }
}
