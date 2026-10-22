using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Features;
using FieldService.Api.Features.Jobs;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Tests.Features.Jobs;

[Collection(DatabaseCollection.Name)]
public sealed class GetJobByIdTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private static readonly Guid _templateId = ChecklistTemplateSeed.TemplateId(2);

    private DateTimeOffset Now => UtcInstant.Truncate(Fixture.Factory.Clock.GetUtcNow());

    [Fact]
    public async Task AnAdministratorReadsAnUnassignedJobWithItsCreatedEntry()
    {
        var data = new TestData(Fixture, Now);
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var created = await data.CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.GetAsync(PathOf(created.Id));

        var job = await ReadJobAsync(response);
        Assert.Equal(created.Id, job.Id);
        Assert.Equal(created.Number, job.Number);
        Assert.Equal(created.Title, job.Title);
        Assert.Equal(JobStatus.Unassigned, job.Status);
        Assert.Equal(new ChecklistTemplateReference(_templateId, "Rooftop HVAC service", Trade.Hvac), job.ChecklistTemplate);
        Assert.Null(job.AssignedTechnician);
        Assert.Null(job.AssignedAt);
        Assert.Equal(new UserReference(administrator.Id, administrator.DisplayName), job.CreatedBy);
        var entry = Assert.Single(job.History);
        Assert.Equal(JobHistoryAction.Created, entry.Action);
        Assert.Equal(new UserReference(administrator.Id, administrator.DisplayName), entry.Actor);
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task AnAdministratorReadsTheAssigneeAndTheWholeHistoryOldestFirst()
    {
        var start = Now;
        var administrator = await new TestData(Fixture, start).CreateAdministratorAsync(CancellationToken);
        var technician = await new TestData(Fixture, start).CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var job = await new TestData(Fixture, start).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        job = await new TestData(Fixture, start.AddMinutes(5)).AssignJobAsync(job, technician, CancellationToken);
        job = await new TestData(Fixture, start.AddMinutes(10)).ChangeStatusAsync(job, JobStatus.InProgress, CancellationToken);
        job = await new TestData(Fixture, start.AddMinutes(20)).ChangeStatusAsync(job, JobStatus.OnHold, CancellationToken, TestData.HoldNote);
        using var client = ClientFor(administrator);

        using var response = await client.GetAsync(PathOf(job.Id));

        var details = await ReadJobAsync(response);
        Assert.Equal(JobStatus.OnHold, details.Status);
        Assert.Equal(new JobTechnicianReference(technician.UserId, technician.User.DisplayName, Trade.Hvac), details.AssignedTechnician);
        Assert.Equal(start.AddMinutes(5), details.AssignedAt);
        Assert.Equal(start.AddMinutes(10), details.StartedAt);
        Assert.Equal(start.AddMinutes(20), details.UpdatedAt);
        Assert.Equal(
            [JobHistoryAction.Created, JobHistoryAction.Assigned, JobHistoryAction.Started, JobHistoryAction.PutOnHold],
            details.History.Select(entry => entry.Action));
        Assert.Equal(
            [start, start.AddMinutes(5), start.AddMinutes(10), start.AddMinutes(20)],
            details.History.Select(entry => entry.OccurredAt));
        var administratorReference = new UserReference(administrator.Id, administrator.DisplayName);
        var technicianReference = new UserReference(technician.UserId, technician.User.DisplayName);
        Assert.Equal(
            [administratorReference, administratorReference, technicianReference, technicianReference],
            details.History.Select(entry => entry.Actor));
        Assert.Equal([null, technicianReference, null, null], details.History.Select(entry => entry.Technician));
        Assert.Equal([null, null, null, TestData.HoldNote], details.History.Select(entry => entry.Note));
        Assert.Equal(
            [null, JobStatus.Unassigned, JobStatus.Assigned, JobStatus.InProgress],
            details.History.Select(entry => entry.FromStatus));
    }

    [Fact]
    public async Task AmongEqualInstantsTheCreatedEntryComesFirstAndTheOthersFollowById()
    {
        var data = new TestData(Fixture, Now);
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var technician = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var job = await data.CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        job = await data.AssignJobAsync(job, technician, CancellationToken);
        job = await data.ChangeStatusAsync(job, JobStatus.InProgress, CancellationToken);
        job = await data.ChangeStatusAsync(job, JobStatus.OnHold, CancellationToken, TestData.HoldNote);
        job = await data.ChangeStatusAsync(job, JobStatus.InProgress, CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.GetAsync(PathOf(job.Id));

        var history = (await ReadJobAsync(response)).History;
        Assert.Equal(5, history.Count);
        Assert.All(history, entry => Assert.Equal(Now, entry.OccurredAt));
        Assert.Equal(JobHistoryAction.Created, history[0].Action);
        var rest = history.Skip(1).Select(entry => entry.Id.ToString()).ToList();
        Assert.Equal(rest.Order(StringComparer.Ordinal), rest);
    }

    [Fact]
    public async Task TheResponseReadsBackFromAFreshDbContextWithTheCurrentVersion()
    {
        var data = new TestData(Fixture, Now);
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var technician = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var job = await data.CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        job = await data.AssignJobAsync(job, technician, CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.GetAsync(PathOf(job.Id));

        var details = await ReadJobAsync(response);
        await using var db = Fixture.CreateAppDbContext();
        var stored = await db.Jobs.AsNoTracking().SingleAsync(entry => entry.Id == job.Id, CancellationToken);
        var storedHistory = await db.JobHistory.AsNoTracking().Where(entry => entry.JobId == job.Id).Select(entry => entry.Id).ToListAsync(CancellationToken);
        Assert.Equal(stored.Version, details.Version);
        Assert.Equal(job.Version, details.Version);
        Assert.Equal(stored.Status, details.Status);
        Assert.Equal(stored.AssignedTechnicianId, details.AssignedTechnician?.Id);
        Assert.Equal(stored.AssignedAt, details.AssignedAt);
        Assert.Equal(stored.UpdatedAt, details.UpdatedAt);
        Assert.Equal(storedHistory.Order(), details.History.Select(entry => entry.Id).Order());
    }

    [Fact]
    public async Task TheAssignedTechnicianReadsTheirJob()
    {
        var data = new TestData(Fixture, Now);
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var technician = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var job = await data.AssignJobAsync(
            await data.CreateJobAsync(administrator.Id, _templateId, CancellationToken),
            technician,
            CancellationToken);
        using var client = ClientFor(technician.User);

        using var response = await client.GetAsync(PathOf(job.Id));

        var details = await ReadJobAsync(response);
        Assert.Equal(job.Id, details.Id);
        Assert.Equal(JobStatus.Assigned, details.Status);
        Assert.Equal(technician.UserId, details.AssignedTechnician?.Id);
    }

    [Fact]
    public async Task ATechnicianStillReadsACancelledJobThatStaysAssignedToThem()
    {
        var data = new TestData(Fixture, Now);
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var technician = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var job = await data.AssignJobAsync(
            await data.CreateJobAsync(administrator.Id, _templateId, CancellationToken),
            technician,
            CancellationToken);
        job = await data.CancelJobAsync(job, CancellationToken);
        using var client = ClientFor(technician.User);

        using var response = await client.GetAsync(PathOf(job.Id));

        var details = await ReadJobAsync(response);
        Assert.Equal(JobStatus.Cancelled, details.Status);
        Assert.Equal(
            TestData.CancellationReason,
            details.History.Single(entry => entry.Action == JobHistoryAction.Cancelled).Note);
    }

    [Fact]
    public async Task ATechnicianGets404JobNotFoundForAJobAssignedToAnotherTechnician()
    {
        var data = new TestData(Fixture, Now);
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var assignee = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var other = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var job = await data.AssignJobAsync(
            await data.CreateJobAsync(administrator.Id, _templateId, CancellationToken),
            assignee,
            CancellationToken);
        using var client = ClientFor(other.User);

        using var response = await client.GetAsync(PathOf(job.Id));

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.NotFound, "job.not-found");
        Assert.Equal("The job was not found.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.5", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(client.LastCorrelationId, problem.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task ATechnicianGetsTheSame404ForAnUnassignedJobAsForAJobThatDoesNotExist()
    {
        var data = new TestData(Fixture, Now);
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var technician = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var unassigned = await data.CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        using var client = ClientFor(technician.User);

        using var forUnassigned = await client.GetAsync(PathOf(unassigned.Id));
        using var forMissing = await client.GetAsync(PathOf(Guid.CreateVersion7()));

        using var first = await ApiClient.AssertProblemAsync(forUnassigned, HttpStatusCode.NotFound, "job.not-found");
        using var second = await ApiClient.AssertProblemAsync(forMissing, HttpStatusCode.NotFound, "job.not-found");
        Assert.Equal(
            first.RootElement.EnumerateObject().Select(member => member.Name),
            second.RootElement.EnumerateObject().Select(member => member.Name));
        Assert.Equal(first.RootElement.GetProperty("title").GetString(), second.RootElement.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData(JobStatus.Unassigned, false, true)]
    [InlineData(JobStatus.Assigned, false, true)]
    [InlineData(JobStatus.InProgress, false, false)]
    [InlineData(JobStatus.OnHold, false, false)]
    [InlineData(JobStatus.Completed, false, false)]
    [InlineData(JobStatus.Cancelled, false, false)]
    [InlineData(JobStatus.Assigned, true, false)]
    [InlineData(JobStatus.InProgress, true, false)]
    [InlineData(JobStatus.OnHold, true, false)]
    [InlineData(JobStatus.Completed, true, false)]
    [InlineData(JobStatus.Cancelled, true, false)]
    public async Task CanAssignFollowsTheTransitionTableForTheCallerAndTheJobStatus(
        JobStatus status,
        bool asAssignee,
        bool canAssign)
    {
        var data = new TestData(Fixture, Now);
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var technician = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var job = await data.MoveToAsync(
            await data.CreateJobAsync(administrator.Id, _templateId, CancellationToken),
            technician,
            status,
            CancellationToken);
        using var client = ClientFor(asAssignee ? technician.User : administrator);

        using var response = await client.GetAsync(PathOf(job.Id));

        var details = await ReadJobAsync(response);
        Assert.Equal(status, details.Status);
        Assert.Equal(canAssign, details.Affordances.CanAssign);
    }

    [Theory]
    [InlineData(JobStatus.Unassigned, false, null, false)]
    [InlineData(JobStatus.Assigned, false, null, false)]
    [InlineData(JobStatus.InProgress, false, null, false)]
    [InlineData(JobStatus.OnHold, false, null, false)]
    [InlineData(JobStatus.Completed, false, null, false)]
    [InlineData(JobStatus.Cancelled, false, null, false)]
    [InlineData(JobStatus.Assigned, true, JobStatus.InProgress, false)]
    [InlineData(JobStatus.InProgress, true, JobStatus.OnHold, true)]
    [InlineData(JobStatus.OnHold, true, JobStatus.InProgress, false)]
    [InlineData(JobStatus.Completed, true, null, false)]
    [InlineData(JobStatus.Cancelled, true, null, false)]
    public async Task AllowedStatusChangesFollowTheTransitionTableForTheCallerAndTheJobStatus(
        JobStatus status,
        bool asAssignee,
        JobStatus? allowedStatus,
        bool requiresNote)
    {
        var data = new TestData(Fixture, Now);
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var technician = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var job = await data.MoveToAsync(
            await data.CreateJobAsync(administrator.Id, _templateId, CancellationToken),
            technician,
            status,
            CancellationToken);
        using var client = ClientFor(asAssignee ? technician.User : administrator);

        using var response = await client.GetAsync(PathOf(job.Id));

        var details = await ReadJobAsync(response);
        Assert.Equal(status, details.Status);
        AllowedStatusChange[] expected = allowedStatus is { } target ? [new(target, requiresNote)] : [];
        Assert.Equal(expected, details.Affordances.AllowedStatusChanges);
    }

    [Fact]
    public async Task TheAffordancesMemberIsLastAndHoldsAllowedStatusChangesThenCanAssign()
    {
        var data = new TestData(Fixture, Now);
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var technician = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var job = await data.MoveToAsync(
            await data.CreateJobAsync(administrator.Id, _templateId, CancellationToken),
            technician,
            JobStatus.InProgress,
            CancellationToken);
        using var client = ClientFor(technician.User);

        using var response = await client.GetAsync(PathOf(job.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var members = document.RootElement.EnumerateObject().Select(member => member.Name).ToList();
        Assert.Equal(["version", "affordances"], members.TakeLast(2));
        var affordances = document.RootElement.GetProperty("affordances");
        Assert.Equal(
            ["allowedStatusChanges", "canAssign"],
            affordances.EnumerateObject().Select(member => member.Name));
        Assert.Equal(
            """[{"status":"OnHold","requiresNote":true}]""",
            affordances.GetProperty("allowedStatusChanges").GetRawText());
        Assert.Equal(JsonValueKind.False, affordances.GetProperty("canAssign").ValueKind);
    }

    [Fact]
    public async Task AnUnknownJobIsA404JobNotFoundWrittenAsProblemJson()
    {
        var administrator = await new TestData(Fixture, Now).CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.GetAsync(PathOf(Guid.CreateVersion7()));

        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        await ApiClient.AssertProblemAsync(response, HttpStatusCode.NotFound, "job.not-found");
    }

    [Fact]
    public async Task AnIdThatIsNotAGuidMatchesNoRoute()
    {
        var administrator = await new TestData(Fixture, Now).CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.GetAsync("/api/jobs/1042");

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.NotFound, "resource.not-found");
    }

    [Fact]
    public async Task AnAnonymousCallerGets401()
    {
        var data = new TestData(Fixture, Now);
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var job = await data.CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.GetAsync(PathOf(job.Id));

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated");
    }

    private static string PathOf(Guid jobId) => $"/api/jobs/{jobId}";

    private static async Task<JobDetailsResponse> ReadJobAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var job = await response.Content.ReadFromJsonAsync<JobDetailsResponse>(ApiClient.Json, TestContext.Current.CancellationToken);
        return job ?? throw new InvalidOperationException("The response has no job body.");
    }

    private ApiClient ClientFor(User user)
    {
        var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TestTokens.For(Fixture.Factory, user);
        return client;
    }
}
