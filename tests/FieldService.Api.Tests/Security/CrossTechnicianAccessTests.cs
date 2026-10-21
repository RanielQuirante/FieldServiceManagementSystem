using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldService.Api.Domain;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Tests.Security;

[Collection(DatabaseCollection.Name)]
public sealed class CrossTechnicianAccessTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private static readonly string[] _requestMembers = ["instance", "traceId", "correlationId"];

    public static TheoryData<JobStatus> AssignedStatuses =>
    [
        JobStatus.Assigned,
        JobStatus.InProgress,
        JobStatus.OnHold,
        JobStatus.Completed,
        JobStatus.Cancelled,
    ];

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
        using var own = await ownerClient.GetAsync($"/api/jobs/{job.Id}");

        using var foreignProblem = await ApiClient.AssertProblemAsync(foreign, HttpStatusCode.NotFound, "job.not-found");
        using var missingProblem = await ApiClient.AssertProblemAsync(missing, HttpStatusCode.NotFound, "job.not-found");
        Assert.Equal(WithoutRequestMembers(missingProblem), WithoutRequestMembers(foreignProblem));
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        using var ownBody = JsonDocument.Parse(await own.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(status.ToString(), ownBody.RootElement.GetProperty("status").GetString());
        Assert.Equal(before, await ReadRowAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();
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

        using var hidden = await technicianClient.GetAsync($"/api/jobs/{job.Id}");
        using var visible = await administratorClient.GetAsync($"/api/jobs/{job.Id}");

        using var problem = await ApiClient.AssertProblemAsync(hidden, HttpStatusCode.NotFound, "job.not-found");
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
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
