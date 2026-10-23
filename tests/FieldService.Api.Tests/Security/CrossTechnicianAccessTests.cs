using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldService.Api.Domain;
using FieldService.Api.Features.Technicians.GetAssignedJobs;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Tests.Security;

[Collection(DatabaseCollection.Name)]
public sealed class CrossTechnicianAccessTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string MyJobsPath = "/api/technicians/me/jobs";

    private static readonly string[] _requestMembers = ["instance", "traceId", "correlationId"];

    private static readonly JobStatus[] _assignedStatuses =
    [
        JobStatus.Assigned,
        JobStatus.InProgress,
        JobStatus.OnHold,
        JobStatus.Completed,
        JobStatus.Cancelled,
    ];

    public static TheoryData<JobStatus> AssignedStatuses => [.. _assignedStatuses];

    public static TheoryData<JobStatus> ChangeableStatuses => [JobStatus.Assigned, JobStatus.InProgress, JobStatus.OnHold];

    public static TheoryData<string?, bool> MyJobsQueries => new()
    {
        { null, false },
        { "active", false },
        { "completed", false },
        { null, true },
        { "active", true },
        { "completed", true },
    };

    [Theory]
    [MemberData(nameof(AssignedStatuses))]
    public async Task ATechnicianGets404ForAnotherTechniciansJobInEveryStatusAndNothingChanges(JobStatus status)
    {
        var data = new TestData(Fixture, Fixture.Factory.Clock.GetUtcNow());
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var owner = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var intruder = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var job = await ArrangeJobAsync(data, administrator, owner, status);
        var before = await ReadRowAsync(job.Id);
        Fixture.Factory.Events.Clear();
        using var intruderClient = Fixture.Factory.CreateApiClient();
        intruderClient.AccessToken = TestTokens.For(Fixture.Factory, intruder.User);
        using var ownerClient = Fixture.Factory.CreateApiClient();
        ownerClient.AccessToken = TestTokens.For(Fixture.Factory, owner.User);

        using var foreign = await intruderClient.GetAsync($"/api/jobs/{job.Id}");
        using var missing = await intruderClient.GetAsync($"/api/jobs/{Guid.CreateVersion7()}");
        using var foreignChange = await intruderClient.PutJsonAsync(StatusPath(job.Id), OwnersNextChange(job));
        using var missingChange = await intruderClient.PutJsonAsync(StatusPath(Guid.CreateVersion7()), OwnersNextChange(job));
        using var own = await ownerClient.GetAsync($"/api/jobs/{job.Id}");

        using var foreignProblem = await ApiClient.AssertProblemAsync(foreign, HttpStatusCode.NotFound, "job.not-found");
        using var missingProblem = await ApiClient.AssertProblemAsync(missing, HttpStatusCode.NotFound, "job.not-found");
        Assert.Equal(WithoutRequestMembers(missingProblem), WithoutRequestMembers(foreignProblem));
        using var foreignChangeProblem = await ApiClient.AssertProblemAsync(foreignChange, HttpStatusCode.NotFound, "job.not-found");
        using var missingChangeProblem = await ApiClient.AssertProblemAsync(missingChange, HttpStatusCode.NotFound, "job.not-found");
        Assert.Equal(WithoutRequestMembers(missingChangeProblem), WithoutRequestMembers(foreignChangeProblem));
        Assert.Equal(WithoutRequestMembers(foreignProblem), WithoutRequestMembers(foreignChangeProblem));
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        using var ownBody = JsonDocument.Parse(await own.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(status.ToString(), ownBody.RootElement.GetProperty("status").GetString());
        Assert.Equal(before, await ReadRowAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Theory]
    [MemberData(nameof(ChangeableStatuses))]
    public async Task AnotherTechniciansChangeGets404WhileTheOwnerMakesTheSameChangeWithTheSameVersion(JobStatus status)
    {
        var data = new TestData(Fixture, Fixture.Factory.Clock.GetUtcNow());
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var owner = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var intruder = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var job = await ArrangeJobAsync(data, administrator, owner, status);
        var before = await ReadRowAsync(job.Id);
        Fixture.Factory.Events.Clear();
        using var intruderClient = Fixture.Factory.CreateApiClient();
        intruderClient.AccessToken = TestTokens.For(Fixture.Factory, intruder.User);
        using var ownerClient = Fixture.Factory.CreateApiClient();
        ownerClient.AccessToken = TestTokens.For(Fixture.Factory, owner.User);

        using var foreignChange = await intruderClient.PutJsonAsync(StatusPath(job.Id), OwnersNextChange(job));

        using var problem = await ApiClient.AssertProblemAsync(foreignChange, HttpStatusCode.NotFound, "job.not-found");
        Assert.Equal(before, await ReadRowAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();

        using var ownChange = await ownerClient.PutJsonAsync(StatusPath(job.Id), OwnersNextChange(job));

        Assert.Equal(HttpStatusCode.OK, ownChange.StatusCode);
        using var ownBody = JsonDocument.Parse(await ownChange.Content.ReadAsStringAsync(CancellationToken));
        var expected = status == JobStatus.InProgress ? JobStatus.OnHold : JobStatus.InProgress;
        Assert.Equal(expected.ToString(), ownBody.RootElement.GetProperty("status").GetString());
        var recorded = Assert.Single(Fixture.Factory.Events.Events);
        Assert.Equal(job.Id, recorded.JobId);
    }

    [Theory]
    [MemberData(nameof(AssignedStatuses))]
    public async Task ATechnicianGets404ForAnotherTechniciansInspectionAndSubmissionInEveryStatusAndNothingChanges(JobStatus status)
    {
        var data = new TestData(Fixture, Fixture.Factory.Clock.GetUtcNow());
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var owner = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var intruder = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var job = await ArrangeJobAsync(data, administrator, owner, status);
        var version = await VersionOfAsync(job.Id);
        var before = await ReadRowAsync(job.Id);
        var inspectionBefore = await ReadInspectionAsync(job.Id);
        var missingId = Guid.CreateVersion7();
        Fixture.Factory.Events.Clear();
        using var intruderClient = Fixture.Factory.CreateApiClient();
        intruderClient.AccessToken = TestTokens.For(Fixture.Factory, intruder.User);
        using var ownerClient = Fixture.Factory.CreateApiClient();
        ownerClient.AccessToken = TestTokens.For(Fixture.Factory, owner.User);

        using var foreignRead = await intruderClient.GetAsync(InspectionPath(job.Id));
        using var missingRead = await intruderClient.GetAsync(InspectionPath(missingId));
        using var foreignSave = await intruderClient.PutJsonAsync(InspectionPath(job.Id), CompleteSave(version));
        using var missingSave = await intruderClient.PutJsonAsync(InspectionPath(missingId), CompleteSave(version));
        using var foreignSubmission = await intruderClient.PostJsonAsync(SubmissionPath(job.Id), Submission(version));
        using var missingSubmission = await intruderClient.PostJsonAsync(SubmissionPath(missingId), Submission(version));
        using var foreignJob = await intruderClient.GetAsync($"/api/jobs/{job.Id}");
        using var own = await ownerClient.GetAsync(InspectionPath(job.Id));

        using var jobProblem = await ApiClient.AssertProblemAsync(foreignJob, HttpStatusCode.NotFound, "job.not-found");
        foreach (var (foreign, missing) in new[] { (foreignRead, missingRead), (foreignSave, missingSave), (foreignSubmission, missingSubmission) })
        {
            using var foreignProblem = await ApiClient.AssertProblemAsync(foreign, HttpStatusCode.NotFound, "job.not-found");
            using var missingProblem = await ApiClient.AssertProblemAsync(missing, HttpStatusCode.NotFound, "job.not-found");
            Assert.Equal(WithoutRequestMembers(missingProblem), WithoutRequestMembers(foreignProblem));
            Assert.Equal(WithoutRequestMembers(jobProblem), WithoutRequestMembers(foreignProblem));
        }

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        using var ownBody = JsonDocument.Parse(await own.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(status.ToString(), ownBody.RootElement.GetProperty("jobStatus").GetString());
        Assert.Equal(before, await ReadRowAsync(job.Id));
        Assert.Equal(inspectionBefore, await ReadInspectionAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task AnotherTechniciansSaveAndSubmissionGet404WhileTheOwnerSavesAndSubmitsWithTheSameVersions()
    {
        var data = new TestData(Fixture, Fixture.Factory.Clock.GetUtcNow());
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var owner = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var intruder = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var job = await ArrangeJobAsync(data, administrator, owner, JobStatus.InProgress);
        Fixture.Factory.Events.Clear();
        using var intruderClient = Fixture.Factory.CreateApiClient();
        intruderClient.AccessToken = TestTokens.For(Fixture.Factory, intruder.User);
        using var ownerClient = Fixture.Factory.CreateApiClient();
        ownerClient.AccessToken = TestTokens.For(Fixture.Factory, owner.User);

        using var foreignSave = await intruderClient.PutJsonAsync(InspectionPath(job.Id), CompleteSave(job.Version));
        using var foreignSaveProblem = await ApiClient.AssertProblemAsync(foreignSave, HttpStatusCode.NotFound, "job.not-found");
        Assert.Equal(job.Version, await VersionOfAsync(job.Id));
        Assert.Equal(string.Empty, await ReadInspectionAsync(job.Id));
        using var ownSave = await ownerClient.PutJsonAsync(InspectionPath(job.Id), CompleteSave(job.Version));
        Assert.Equal(HttpStatusCode.OK, ownSave.StatusCode);
        using var savedBody = JsonDocument.Parse(await ownSave.Content.ReadAsStringAsync(CancellationToken));
        var saved = savedBody.RootElement.GetProperty("version").GetUInt32();
        Assert.Equal(10, savedBody.RootElement.GetProperty("progress").GetProperty("answered").GetInt32());

        using var foreignSubmission = await intruderClient.PostJsonAsync(SubmissionPath(job.Id), Submission(saved));
        using var foreignSubmissionProblem = await ApiClient.AssertProblemAsync(foreignSubmission, HttpStatusCode.NotFound, "job.not-found");
        Assert.Equal(saved, await VersionOfAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();
        using var ownSubmission = await ownerClient.PostJsonAsync(SubmissionPath(job.Id), Submission(saved));

        Assert.Equal(HttpStatusCode.OK, ownSubmission.StatusCode);
        using var submittedBody = JsonDocument.Parse(await ownSubmission.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(nameof(JobStatus.Completed), submittedBody.RootElement.GetProperty("status").GetString());
        var recorded = Assert.Single(Fixture.Factory.Events.Events);
        Assert.Equal(job.Id, recorded.JobId);
    }

    [Fact]
    public async Task AnUnassignedJobIsInvisibleToEveryTechnicianAndVisibleToTheAdministrator()
    {
        var data = new TestData(Fixture, Fixture.Factory.Clock.GetUtcNow());
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var technician = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var job = await data.CreateJobAsync(administrator.Id, TestData.HvacTemplateId, CancellationToken);
        using var technicianClient = Fixture.Factory.CreateApiClient();
        technicianClient.AccessToken = TestTokens.For(Fixture.Factory, technician.User);
        using var administratorClient = Fixture.Factory.CreateApiClient();
        administratorClient.AccessToken = TestTokens.For(Fixture.Factory, administrator);

        var before = await ReadRowAsync(job.Id);
        Fixture.Factory.Events.Clear();

        using var hidden = await technicianClient.GetAsync($"/api/jobs/{job.Id}");
        using var hiddenChange = await technicianClient.PutJsonAsync(
            StatusPath(job.Id),
            new { status = "InProgress", note = (string?)null, version = job.Version });
        using var visible = await administratorClient.GetAsync($"/api/jobs/{job.Id}");
        using var hiddenInspection = await technicianClient.GetAsync(InspectionPath(job.Id));
        using var hiddenSave = await technicianClient.PutJsonAsync(InspectionPath(job.Id), CompleteSave(job.Version));
        using var hiddenSubmission = await technicianClient.PostJsonAsync(SubmissionPath(job.Id), Submission(job.Version));
        using var visibleInspection = await administratorClient.GetAsync(InspectionPath(job.Id));

        using var problem = await ApiClient.AssertProblemAsync(hidden, HttpStatusCode.NotFound, "job.not-found");
        using var changeProblem = await ApiClient.AssertProblemAsync(hiddenChange, HttpStatusCode.NotFound, "job.not-found");
        using var inspectionProblem = await ApiClient.AssertProblemAsync(hiddenInspection, HttpStatusCode.NotFound, "job.not-found");
        using var saveProblem = await ApiClient.AssertProblemAsync(hiddenSave, HttpStatusCode.NotFound, "job.not-found");
        using var submissionProblem = await ApiClient.AssertProblemAsync(hiddenSubmission, HttpStatusCode.NotFound, "job.not-found");
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
        Assert.Equal(HttpStatusCode.OK, visibleInspection.StatusCode);
        Assert.Equal(before, await ReadRowAsync(job.Id));
        Assert.Equal(string.Empty, await ReadInspectionAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Theory]
    [MemberData(nameof(MyJobsQueries))]
    public async Task MyJobsNeverListsAnotherTechniciansJobInAnyStatusEvenWithTheirIdInTheQuery(string? view, bool withOwnersId)
    {
        var data = new TestData(Fixture, Fixture.Factory.Clock.GetUtcNow());
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var owner = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var caller = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var ownersJobs = new Dictionary<JobStatus, Guid>();
        foreach (var status in _assignedStatuses)
        {
            ownersJobs[status] = (await ArrangeJobAsync(data, administrator, owner, status)).Id;
        }

        var unassigned = await data.CreateJobAsync(administrator.Id, TestData.HvacTemplateId, CancellationToken);
        var callersActive = await ArrangeJobAsync(data, administrator, caller, JobStatus.Assigned);
        var callersCompleted = await ArrangeJobAsync(data, administrator, caller, JobStatus.Cancelled);
        var completedView = view == "completed";
        Fixture.Factory.Events.Clear();
        using var callerClient = Fixture.Factory.CreateApiClient();
        callerClient.AccessToken = TestTokens.For(Fixture.Factory, caller.User);
        using var ownerClient = Fixture.Factory.CreateApiClient();
        ownerClient.AccessToken = TestTokens.For(Fixture.Factory, owner.User);

        using var listed = await callerClient.GetAsync(MyJobsPath + MyJobsQuery(view, withOwnersId ? owner.UserId : null));
        using var ownersList = await ownerClient.GetAsync(MyJobsPath + MyJobsQuery(view, technicianId: null));

        var callersIds = await ReadMyJobIdsAsync(listed);
        Assert.Equal([completedView ? callersCompleted.Id : callersActive.Id], callersIds);
        Assert.Empty(callersIds.Intersect(ownersJobs.Values.Append(unassigned.Id)));
        var expectedOwners = ownersJobs
            .Where(entry => IsFinished(entry.Key) == completedView)
            .Select(entry => entry.Value)
            .Order();
        Assert.Equal(expectedOwners, (await ReadMyJobIdsAsync(ownersList)).Order());
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task AJobReassignedAwayFromATechnicianIsA404ForThemAndLeavesTheirListWhileTheNewAssigneeReadsIt()
    {
        var data = new TestData(Fixture, Fixture.Factory.Clock.GetUtcNow());
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var previous = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var next = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var job = await ArrangeJobAsync(data, administrator, previous, JobStatus.Assigned);
        using var previousClient = Fixture.Factory.CreateApiClient();
        previousClient.AccessToken = TestTokens.For(Fixture.Factory, previous.User);
        using var nextClient = Fixture.Factory.CreateApiClient();
        nextClient.AccessToken = TestTokens.For(Fixture.Factory, next.User);
        using var administratorClient = Fixture.Factory.CreateApiClient();
        administratorClient.AccessToken = TestTokens.For(Fixture.Factory, administrator);
        using var readBefore = await previousClient.GetAsync($"/api/jobs/{job.Id}");
        Assert.Equal(HttpStatusCode.OK, readBefore.StatusCode);
        using var reassigned = await administratorClient.PutJsonAsync(
            $"/api/jobs/{job.Id}/assignment",
            new { technicianId = next.UserId, version = job.Version });
        Assert.Equal(HttpStatusCode.OK, reassigned.StatusCode);
        using var reassignedBody = JsonDocument.Parse(await reassigned.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(next.UserId, reassignedBody.RootElement.GetProperty("assignedTechnician").GetProperty("id").GetGuid());
        var after = await ReadRowAsync(job.Id);
        Fixture.Factory.Events.Clear();

        using var foreign = await previousClient.GetAsync($"/api/jobs/{job.Id}");
        using var missing = await previousClient.GetAsync($"/api/jobs/{Guid.CreateVersion7()}");
        using var foreignChange = await previousClient.PutJsonAsync(
            StatusPath(job.Id),
            new { status = "InProgress", note = (string?)null, version = reassignedBody.RootElement.GetProperty("version").GetUInt32() });
        using var previousActive = await previousClient.GetAsync(MyJobsPath);
        using var previousCompleted = await previousClient.GetAsync(MyJobsPath + "?view=completed");
        using var own = await nextClient.GetAsync($"/api/jobs/{job.Id}");
        using var nextActive = await nextClient.GetAsync(MyJobsPath);
        using var foreignInspection = await previousClient.GetAsync(InspectionPath(job.Id));
        using var ownInspection = await nextClient.GetAsync(InspectionPath(job.Id));

        using var foreignInspectionProblem = await ApiClient.AssertProblemAsync(foreignInspection, HttpStatusCode.NotFound, "job.not-found");
        Assert.Equal(HttpStatusCode.OK, ownInspection.StatusCode);
        using var foreignProblem = await ApiClient.AssertProblemAsync(foreign, HttpStatusCode.NotFound, "job.not-found");
        using var missingProblem = await ApiClient.AssertProblemAsync(missing, HttpStatusCode.NotFound, "job.not-found");
        Assert.Equal(WithoutRequestMembers(missingProblem), WithoutRequestMembers(foreignProblem));
        using var foreignChangeProblem = await ApiClient.AssertProblemAsync(foreignChange, HttpStatusCode.NotFound, "job.not-found");
        Assert.Equal(WithoutRequestMembers(foreignProblem), WithoutRequestMembers(foreignChangeProblem));
        Assert.Empty(await ReadMyJobIdsAsync(previousActive));
        Assert.Empty(await ReadMyJobIdsAsync(previousCompleted));
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal([job.Id], await ReadMyJobIdsAsync(nextActive));
        Assert.Equal(after, await ReadRowAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();
    }

    private static bool IsFinished(JobStatus status) => status is JobStatus.Completed or JobStatus.Cancelled;

    private static string StatusPath(Guid jobId) => $"/api/jobs/{jobId}/status";

    private static string InspectionPath(Guid jobId) => $"/api/jobs/{jobId}/inspection";

    private static string SubmissionPath(Guid jobId) => $"/api/jobs/{jobId}/submission";

    private static object CompleteSave(uint version) => new
    {
        answers = TestData.ChecklistOf(TestData.HvacTemplateId).Items
            .Select(item => new
            {
                checklistItemId = item.Id,
                result = "Pass",
                readings = item.ReadingFieldIds.Select(fieldId => new { readingFieldId = fieldId, value = 12.5m }).ToArray(),
            })
            .ToArray(),
        version,
    };

    private static object Submission(uint version) => new { technicianNotes = "Unit running normally.", siteSafetyConfirmed = true, version };

    private async Task<uint> VersionOfAsync(Guid jobId)
    {
        await using var db = Fixture.CreateAppDbContext();
        return (await db.Jobs.AsNoTracking().SingleAsync(entry => entry.Id == jobId, CancellationToken)).Version;
    }

    private async Task<string> ReadInspectionAsync(Guid jobId)
    {
        await using var db = Fixture.CreateAppDbContext();
        var inspection = await db.Inspections.AsNoTracking()
            .Include(entry => entry.Answers)
            .ThenInclude(answer => answer.Readings)
            .SingleOrDefaultAsync(entry => entry.JobId == jobId, CancellationToken);
        return inspection is null
            ? string.Empty
            : $"{inspection.UpdatedAt:O}|{inspection.SubmittedAt:O}|{inspection.TechnicianNotes}|"
                + string.Join(';', inspection.Answers
                    .OrderBy(answer => answer.ChecklistItemId)
                    .Select(answer => $"{answer.ChecklistItemId}:{answer.Result}:{answer.Note}:{answer.AnsweredAt:O}:{answer.Readings.Count}"));
    }

    private static object OwnersNextChange(Job job) => job.Status == JobStatus.InProgress
        ? new { status = "OnHold", note = (string?)TestData.HoldNote, version = job.Version }
        : new { status = "InProgress", note = (string?)null, version = job.Version };

    private static string MyJobsQuery(string? view, Guid? technicianId)
    {
        var parameters = new List<string>();
        if (view is not null)
        {
            parameters.Add($"view={view}");
        }

        if (technicianId is { } id)
        {
            parameters.Add($"technicianId={id}");
        }

        return parameters.Count == 0 ? string.Empty : "?" + string.Join('&', parameters);
    }

    private async Task<List<Guid>> ReadMyJobIdsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<GetAssignedJobsResponse>(ApiClient.Json, CancellationToken);
        Assert.NotNull(body);
        return [.. body.Items.Select(item => item.Id)];
    }

    private static async Task<Job> ArrangeJobAsync(TestData data, User administrator, Technician owner, JobStatus status)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await data.CreateJobAsync(administrator.Id, TestData.HvacTemplateId, cancellationToken);
        job = await data.AssignJobAsync(job, owner, cancellationToken);
        switch (status)
        {
            case JobStatus.InProgress:
                return await data.ChangeStatusAsync(job, JobStatus.InProgress, cancellationToken);
            case JobStatus.OnHold:
                job = await data.ChangeStatusAsync(job, JobStatus.InProgress, cancellationToken);
                return await data.ChangeStatusAsync(job, JobStatus.OnHold, cancellationToken, TestData.HoldNote);
            case JobStatus.Completed:
                await data.CreateInspectionAsync(job, owner, submitted: true, cancellationToken);
                return job;
            case JobStatus.Cancelled:
                return await data.CancelJobAsync(job, cancellationToken);
            default:
                return job;
        }
    }

    private async Task<string> ReadRowAsync(Guid jobId)
    {
        await using var db = Fixture.CreateAppDbContext();
        var job = await db.Jobs.AsNoTracking().SingleAsync(entry => entry.Id == jobId, CancellationToken);
        var history = await db.JobHistory.AsNoTracking().CountAsync(entry => entry.JobId == jobId, CancellationToken);
        return $"{job.Status}|{job.Version}|{job.UpdatedAt:O}|{job.AssignedTechnicianId}|{history}";
    }

    private static string WithoutRequestMembers(JsonDocument problem)
    {
        var node = JsonNode.Parse(problem.RootElement.GetRawText())!.AsObject();
        foreach (var member in _requestMembers)
        {
            node.Remove(member);
        }

        return node.ToJsonString();
    }
}
