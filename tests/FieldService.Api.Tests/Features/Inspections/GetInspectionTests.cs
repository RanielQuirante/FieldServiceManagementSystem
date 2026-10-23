using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Features;
using FieldService.Api.Features.Inspections;
using FieldService.Api.Features.Jobs;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Tests.Features.Inspections;

[Collection(DatabaseCollection.Name)]
public sealed class GetInspectionTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string Incomplete = "inspection.incomplete";

    private const string Invalid = "job.invalid-transition";

    private const string Forbidden = "auth.forbidden";

    private const string FanMotorNote = "Bearing noise at start-up. Recommend motor replacement within 30 days.";

    private static readonly Guid _templateId = ChecklistTemplateSeed.TemplateId(2);

    private static readonly Guid _suction = ChecklistTemplateSeed.ReadingFieldId(2, 3, 1);

    private static readonly Guid _discharge = ChecklistTemplateSeed.ReadingFieldId(2, 3, 2);

    private static readonly Guid _supplyAir = ChecklistTemplateSeed.ReadingFieldId(2, 8, 1);

    private DateTimeOffset Now => UtcInstant.Truncate(Fixture.Factory.Clock.GetUtcNow());

    private DateTimeOffset Earlier => Now.AddMinutes(-30);

    [Fact]
    public async Task BeforeTheFirstSaveTheAssigneeGetsEveryItemAndReadingFieldInPositionOrderWithoutAnswers()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        using var client = ClientFor(technician.User);

        using var response = await client.GetAsync(PathOf(job.Id));

        var inspection = await ReadAsync(response);
        Assert.Equal(job.Id, inspection.JobId);
        Assert.Equal(job.Number, inspection.JobNumber);
        Assert.Equal(JobStatus.InProgress, inspection.JobStatus);
        Assert.Equal(new ChecklistTemplateReference(_templateId, "Rooftop HVAC service", Trade.Hvac), inspection.Template);
        Assert.Equal(Enumerable.Range(1, 10), inspection.Items.Select(item => item.Position));
        Assert.Equal(
            Enumerable.Range(1, 10).Select(position => ChecklistTemplateSeed.ItemId(2, position)),
            inspection.Items.Select(item => item.ChecklistItemId));
        Assert.Equal("Lock out and tag out unit power", inspection.Items[0].Prompt);
        Assert.Equal("Restore power and confirm normal operation", inspection.Items[9].Prompt);
        Assert.Equal(
            [
                new InspectionReadingFieldResponse(_suction, 1, "Suction", "psi"),
                new InspectionReadingFieldResponse(_discharge, 2, "Discharge", "psi"),
            ],
            inspection.Items[2].ReadingFields);
        Assert.Equal([new InspectionReadingFieldResponse(_supplyAir, 1, "Supply air temperature", "°F")], inspection.Items[7].ReadingFields);
        Assert.All(
            inspection.Items.Where(item => item.Position is not (3 or 8)),
            item => Assert.Empty(item.ReadingFields));
        Assert.All(inspection.Items, item => Assert.Null(item.Answer));
        Assert.Equal(new InspectionProgress(0, 10, 0, 0, 0), inspection.Progress);
        Assert.Equal(job.StartedAt, inspection.StartedAt);
        Assert.Null(inspection.CompletedAt);
        Assert.Null(inspection.UpdatedAt);
        Assert.Null(inspection.SubmittedAt);
        Assert.Null(inspection.TechnicianNotes);
        Assert.Equal(job.Version, inspection.Version);
        Assert.Equal(new InspectionAffordances(true, new SubmitAffordance(false, Incomplete, 0, 10)), inspection.Affordances);
    }

    [Fact]
    public async Task AfterSavesTheAnswersAndReadingsComeBackInItemAndFieldOrderWithTheCountsPerResult()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        var firstSave = Earlier.AddMinutes(10);
        var secondSave = Earlier.AddMinutes(20);
        await DataAt(firstSave).SaveInspectionAsync(
            job,
            [
                Answer(6, InspectionResult.NotApplicable, "Direct-drive unit"),
                Answer(3, InspectionResult.Pass, null, new ChecklistReading(_discharge, 285m), new ChecklistReading(_suction, 118m)),
                Answer(1, InspectionResult.Pass, null),
            ],
            CancellationToken);
        await DataAt(secondSave).SaveInspectionAsync(
            job,
            [Answer(5, InspectionResult.Fail, FanMotorNote), Answer(1, InspectionResult.Pass, null)],
            CancellationToken);
        using var client = ClientFor(technician.User);

        using var response = await client.GetAsync(PathOf(job.Id));

        var inspection = await ReadAsync(response);
        Assert.Equal([1, 3, 5, 6], inspection.Items.Where(item => item.Answer is not null).Select(item => item.Position));
        var pressures = inspection.Items[2].Answer!;
        Assert.Equal(InspectionResult.Pass, pressures.Result);
        Assert.Null(pressures.Note);
        Assert.Equal([new InspectionReadingResponse(_suction, 118m), new InspectionReadingResponse(_discharge, 285m)], pressures.Readings);
        Assert.Equal(firstSave, pressures.AnsweredAt);
        var fanMotor = inspection.Items[4].Answer!;
        Assert.Equal(InspectionResult.Fail, fanMotor.Result);
        Assert.Equal(FanMotorNote, fanMotor.Note);
        Assert.Empty(fanMotor.Readings);
        Assert.Equal(secondSave, fanMotor.AnsweredAt);
        Assert.Equal(InspectionResult.NotApplicable, inspection.Items[5].Answer!.Result);
        Assert.Equal("Direct-drive unit", inspection.Items[5].Answer!.Note);
        Assert.Equal(firstSave, inspection.Items[0].Answer!.AnsweredAt);
        Assert.Equal(new InspectionProgress(4, 10, 2, 1, 1), inspection.Progress);
        Assert.Equal(secondSave, inspection.UpdatedAt);
        Assert.Null(inspection.SubmittedAt);
        Assert.Equal((await LoadJobAsync(job.Id)).Version, inspection.Version);
        Assert.Equal(new InspectionAffordances(true, new SubmitAffordance(false, Incomplete, 4, 10)), inspection.Affordances);
    }

    [Fact]
    public async Task AfterSubmissionTheInspectionCarriesSubmittedAtAndTheNotesAndCanNoLongerBeEdited()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        var submittedAt = Earlier.AddMinutes(20);
        await DataAt(Earlier.AddMinutes(10)).SaveInspectionAsync(
            job,
            TestData.PassingAnswers(TestData.ChecklistOf(_templateId)),
            CancellationToken);
        await DataAt(submittedAt).SubmitJobAsync(job, CancellationToken, "  Recharged the system and cleaned the coil.  ");
        using var client = ClientFor(technician.User);

        using var response = await client.GetAsync(PathOf(job.Id));

        var inspection = await ReadAsync(response);
        Assert.Equal(JobStatus.Completed, inspection.JobStatus);
        Assert.Equal(submittedAt, inspection.SubmittedAt);
        Assert.Equal(submittedAt, inspection.CompletedAt);
        Assert.Equal(submittedAt, inspection.UpdatedAt);
        Assert.Equal("Recharged the system and cleaned the coil.", inspection.TechnicianNotes);
        Assert.Equal(new InspectionProgress(10, 10, 10, 0, 0), inspection.Progress);
        Assert.Equal(new InspectionAffordances(false, new SubmitAffordance(false, Invalid, 10, 10)), inspection.Affordances);
    }

    [Theory]
    [InlineData(JobStatus.Assigned, true, false, Invalid, 0)]
    [InlineData(JobStatus.InProgress, true, true, Incomplete, 0)]
    [InlineData(JobStatus.OnHold, true, false, Invalid, 0)]
    [InlineData(JobStatus.Completed, true, false, Invalid, 10)]
    [InlineData(JobStatus.Cancelled, true, false, Invalid, 0)]
    [InlineData(JobStatus.Unassigned, false, false, Forbidden, 0)]
    [InlineData(JobStatus.Assigned, false, false, Forbidden, 0)]
    [InlineData(JobStatus.InProgress, false, false, Forbidden, 0)]
    [InlineData(JobStatus.OnHold, false, false, Forbidden, 0)]
    [InlineData(JobStatus.Completed, false, false, Forbidden, 10)]
    [InlineData(JobStatus.Cancelled, false, false, Forbidden, 0)]
    public async Task CanEditAndCanSubmitFollowTheCallerAndTheJobStatus(
        JobStatus status,
        bool asAssignee,
        bool canEdit,
        string blockingCode,
        int answered)
    {
        var (technician, job) = await ArrangeAsync(status);
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(asAssignee ? technician.User : administrator);

        using var response = await client.GetAsync(PathOf(job.Id));

        var inspection = await ReadAsync(response);
        Assert.Equal(status, inspection.JobStatus);
        Assert.Equal(answered, inspection.Progress.Answered);
        Assert.Equal(new InspectionAffordances(canEdit, new SubmitAffordance(false, blockingCode, answered, 10)), inspection.Affordances);
    }

    [Theory]
    [InlineData(true, 6)]
    [InlineData(true, 10)]
    [InlineData(false, 6)]
    public async Task TheVersionAndCanSubmitEqualTheJobDetailsOfTheSameJob(bool asAssignee, int answeredItems)
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        await DataAt(Earlier.AddMinutes(10)).SaveInspectionAsync(
            job,
            TestData.PassingAnswers(TestData.ChecklistOf(_templateId), answeredItems),
            CancellationToken);
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(asAssignee ? technician.User : administrator);

        using var inspectionResponse = await client.GetAsync(PathOf(job.Id));
        using var jobResponse = await client.GetAsync($"/api/jobs/{job.Id}");

        var inspection = await ReadAsync(inspectionResponse);
        var details = await jobResponse.Content.ReadFromJsonAsync<JobDetailsResponse>(ApiClient.Json, CancellationToken);
        Assert.Equal(details!.Version, inspection.Version);
        Assert.Equal(details.Affordances.CanSubmit, inspection.Affordances.CanSubmit);
        Assert.Equal(answeredItems, inspection.Affordances.CanSubmit.Answered);
        Assert.Equal(asAssignee && answeredItems == 10, inspection.Affordances.CanSubmit.Allowed);
    }

    [Fact]
    public async Task TheResponseWritesEveryMemberInContractOrderWithReadingsAtTwoDecimalsAndNullsPresent()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        await DataAt(Earlier.AddMinutes(10)).SaveInspectionAsync(
            job,
            [Answer(8, InspectionResult.Pass, null, new ChecklistReading(_supplyAir, 54m))],
            CancellationToken);
        using var client = ClientFor(technician.User);

        using var response = await client.GetAsync(PathOf(job.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var root = document.RootElement;
        Assert.Equal(
            [
                "jobId", "jobNumber", "jobStatus", "template", "items", "progress", "startedAt", "completedAt",
                "updatedAt", "submittedAt", "technicianNotes", "version", "affordances",
            ],
            root.EnumerateObject().Select(member => member.Name));
        var item = root.GetProperty("items")[7];
        Assert.Equal(["checklistItemId", "position", "prompt", "readingFields", "answer"], item.EnumerateObject().Select(member => member.Name));
        Assert.Equal(["readingFieldId", "position", "label", "unit"], item.GetProperty("readingFields")[0].EnumerateObject().Select(member => member.Name));
        var answer = item.GetProperty("answer");
        Assert.Equal(["result", "note", "readings", "answeredAt"], answer.EnumerateObject().Select(member => member.Name));
        Assert.Equal("Pass", answer.GetProperty("result").GetString());
        Assert.Equal(JsonValueKind.Null, answer.GetProperty("note").ValueKind);
        Assert.Equal($$"""[{"readingFieldId":"{{_supplyAir}}","value":54.00}]""", answer.GetProperty("readings").GetRawText());
        Assert.Equal(ApiClient.WireInstant(Earlier.AddMinutes(10)), answer.GetProperty("answeredAt").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("items")[0].GetProperty("answer").ValueKind);
        Assert.Equal(["answered", "total", "passCount", "failCount", "notApplicableCount"], root.GetProperty("progress").EnumerateObject().Select(member => member.Name));
        Assert.Equal(JsonValueKind.Null, root.GetProperty("completedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("submittedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("technicianNotes").ValueKind);
        Assert.Equal(ApiClient.WireInstant(Earlier.AddMinutes(10)), root.GetProperty("updatedAt").GetString());
        var affordances = root.GetProperty("affordances");
        Assert.Equal(["canEdit", "canSubmit"], affordances.EnumerateObject().Select(member => member.Name));
        Assert.Equal(
            """{"allowed":false,"blockingCode":"inspection.incomplete","answered":1,"total":10}""",
            affordances.GetProperty("canSubmit").GetRawText());
    }

    [Fact]
    public async Task ReadingTheInspectionWritesNothingAndQueuesNoEvent()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        using var technicianClient = ClientFor(technician.User);
        using var administratorClient = ClientFor(administrator);

        using var first = await technicianClient.GetAsync(PathOf(job.Id));
        using var second = await administratorClient.GetAsync(PathOf(job.Id));

        Assert.Equal(job.Version, (await ReadAsync(first)).Version);
        Assert.Equal(job.Version, (await ReadAsync(second)).Version);
        var stored = await LoadJobAsync(job.Id);
        Assert.Equal(job.Version, stored.Version);
        await using var db = Fixture.CreateAppDbContext();
        Assert.False(await db.Inspections.AnyAsync(entry => entry.JobId == job.Id, CancellationToken));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task AnotherTechniciansJobAnUnassignedJobAndAMissingJobReturnTheSame404()
    {
        var (_, job) = await ArrangeAsync(JobStatus.InProgress);
        var (_, unassigned) = await ArrangeAsync(JobStatus.Unassigned);
        var other = await DataAt(Earlier).CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        using var client = ClientFor(other.User);

        using var forOther = await client.GetAsync(PathOf(job.Id));
        using var forUnassigned = await client.GetAsync(PathOf(unassigned.Id));
        using var forMissing = await client.GetAsync(PathOf(Guid.CreateVersion7()));

        using var first = await ApiClient.AssertProblemAsync(forOther, HttpStatusCode.NotFound, "job.not-found");
        using var second = await ApiClient.AssertProblemAsync(forUnassigned, HttpStatusCode.NotFound, "job.not-found");
        using var third = await ApiClient.AssertProblemAsync(forMissing, HttpStatusCode.NotFound, "job.not-found");
        foreach (var problem in new[] { first, second })
        {
            Assert.Equal(
                third.RootElement.EnumerateObject().Select(member => member.Name),
                problem.RootElement.EnumerateObject().Select(member => member.Name));
            Assert.False(problem.RootElement.TryGetProperty("detail", out _));
        }
    }

    [Fact]
    public async Task AJobIdThatIsNotAGuidMatchesNoRoute()
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.GetAsync("/api/jobs/1042/inspection");

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.NotFound, "resource.not-found");
    }

    [Fact]
    public async Task AnAnonymousCallerAnExpiredTokenAndATokenSignedWithAnotherKeyGet401()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        await using var expiring = Fixture.CreateFactory();
        await using var foreign = Fixture.CreateFactory();
        using var anonymous = Fixture.Factory.CreateApiClient();
        using var expired = expiring.CreateApiClient();
        expired.AccessToken = TestTokens.For(expiring, technician.User);
        expiring.Clock.Advance(TimeSpan.FromMinutes(16));
        using var signedElsewhere = Fixture.Factory.CreateApiClient();
        signedElsewhere.AccessToken = TestTokens.For(foreign, technician.User);

        using var withoutToken = await anonymous.GetAsync(PathOf(job.Id));
        using var withExpired = await expired.GetAsync(PathOf(job.Id));
        using var withForeign = await signedElsewhere.GetAsync(PathOf(job.Id));

        await ApiClient.AssertProblemAsync(withoutToken, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        await ApiClient.AssertProblemAsync(withExpired, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        await ApiClient.AssertProblemAsync(withForeign, HttpStatusCode.Unauthorized, "auth.unauthenticated");
    }

    public static string PathOf(Guid jobId) => $"/api/jobs/{jobId}/inspection";

    private static ChecklistAnswer Answer(int position, InspectionResult result, string? note, params ChecklistReading[] readings) =>
        new(ChecklistTemplateSeed.ItemId(2, position), result, note, readings);

    private static async Task<InspectionResponse> ReadAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var inspection = await response.Content.ReadFromJsonAsync<InspectionResponse>(ApiClient.Json, TestContext.Current.CancellationToken);
        return inspection ?? throw new InvalidOperationException("The response has no inspection body.");
    }

    private async Task<(Technician Technician, Job Job)> ArrangeAsync(JobStatus status)
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var technician = await DataAt(Earlier).CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var created = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        var job = await DataAt(Earlier.AddMinutes(5)).MoveToAsync(created, technician, status, CancellationToken);
        return (technician, job);
    }

    private async Task<Job> LoadJobAsync(Guid jobId)
    {
        await using var db = Fixture.CreateAppDbContext();
        return await db.Jobs.AsNoTracking().SingleAsync(entry => entry.Id == jobId, CancellationToken);
    }

    private TestData DataAt(DateTimeOffset now) => new(Fixture, now);

    private ApiClient ClientFor(User user)
    {
        var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TestTokens.For(Fixture.Factory, user);
        return client;
    }
}
