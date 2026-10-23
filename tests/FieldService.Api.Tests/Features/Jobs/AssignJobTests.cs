using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Features;
using FieldService.Api.Features.Jobs;
using FieldService.Api.Features.Technicians.GetAssignedJobs;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Shared.Realtime;
using FieldService.Api.Tests.Infrastructure;
using FieldService.Api.Tests.Shared.Realtime;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldService.Api.Tests.Features.Jobs;

[Collection(DatabaseCollection.Name)]
public sealed class AssignJobTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private static readonly Guid _templateId = ChecklistTemplateSeed.TemplateId(1);

    private DateTimeOffset Now => UtcInstant.Truncate(Fixture.Factory.Clock.GetUtcNow());

    private DateTimeOffset Earlier => Now.AddMinutes(-30);

    [Fact]
    public async Task AssigningAnUnassignedJobStoresTheAssigneeAndAddsAnAssignedEntryWithANewVersion()
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var technician = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var job = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(technician.UserId, job.Version));

        var details = await ReadJobAsync(response);
        Assert.Equal(job.Id, details.Id);
        Assert.Equal(JobStatus.Assigned, details.Status);
        Assert.Equal(new JobTechnicianReference(technician.UserId, technician.User.DisplayName, Trade.Electrical), details.AssignedTechnician);
        Assert.Equal(Now, details.AssignedAt);
        Assert.Equal(Now, details.UpdatedAt);
        Assert.Equal(Earlier, details.CreatedAt);
        Assert.NotEqual(job.Version, details.Version);
        Assert.Equal([JobHistoryAction.Created, JobHistoryAction.Assigned], details.History.Select(entry => entry.Action));
        var assigned = details.History[1];
        Assert.Equal(JobStatus.Unassigned, assigned.FromStatus);
        Assert.Equal(JobStatus.Assigned, assigned.ToStatus);
        Assert.Equal(new UserReference(administrator.Id, administrator.DisplayName), assigned.Actor);
        Assert.Equal(new UserReference(technician.UserId, technician.User.DisplayName), assigned.Technician);
        Assert.Null(assigned.Note);
        Assert.Equal(Now, assigned.OccurredAt);
        Assert.True(details.Affordances.CanAssign);
    }

    [Fact]
    public async Task TheAssignmentReadsBackThroughAFreshDbContextWithTheReturnedVersion()
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var technician = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var job = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(technician.UserId, job.Version));

        var details = await ReadJobAsync(response);
        await using var db = Fixture.CreateAppDbContext();
        var stored = await db.Jobs.AsNoTracking().Include(entry => entry.History).SingleAsync(entry => entry.Id == job.Id, CancellationToken);
        Assert.Equal(JobStatus.Assigned, stored.Status);
        Assert.Equal(technician.UserId, stored.AssignedTechnicianId);
        Assert.Equal(Now, stored.AssignedAt);
        Assert.Equal(Now, stored.UpdatedAt);
        Assert.Equal(details.Version, stored.Version);
        var entry = Assert.Single(stored.History, entry => entry.Action == JobHistoryAction.Assigned);
        Assert.Equal(details.History[1].Id, entry.Id);
        Assert.Equal(administrator.Id, entry.ActorUserId);
        Assert.Equal(technician.UserId, entry.TechnicianId);
        Assert.Equal(JobStatus.Unassigned, entry.FromStatus);
        Assert.Equal(Now, entry.OccurredAt);
    }

    [Fact]
    public async Task OneJobAssignedEventIsRecordedOnlyAfterTheCommit()
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var technician = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var job = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        using var client = ClientFor(administrator);
        bool? committedWhenEnqueued = null;
        Fixture.Factory.Events.OnEnqueue = jobEvent =>
        {
            using var db = Fixture.CreateAppDbContext();
            committedWhenEnqueued = db.Jobs.AsNoTracking()
                .Any(entry => entry.Id == jobEvent.JobId && entry.AssignedTechnicianId == technician.UserId);
        };

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(technician.UserId, job.Version));

        var details = await ReadJobAsync(response);
        var recorded = Assert.Single(Fixture.Factory.Events.Events);
        Assert.True(committedWhenEnqueued);
        Assert.Equal(JobEventTypes.Assigned, recorded.Type);
        Assert.Equal(JobEvent.CurrentVersion, recorded.Version);
        Assert.Equal(job.Id, recorded.JobId);
        Assert.Equal(job.Number, recorded.JobNumber);
        Assert.Equal(JobStatus.Assigned, recorded.Status);
        Assert.Equal(JobStatus.Unassigned, recorded.PreviousStatus);
        Assert.Equal(details.UpdatedAt, recorded.OccurredAt);
        Assert.Equal(client.LastCorrelationId, recorded.CorrelationId);
    }

    [Fact]
    public async Task ReassigningAnAssignedJobMovesItToTheOtherTechnicianWithAnAssignedEntryFromAssigned()
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var first = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var second = await DataAt(Earlier).CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var job = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        job = await DataAt(Earlier.AddMinutes(5)).AssignJobAsync(job, first, CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(second.UserId, job.Version));

        var details = await ReadJobAsync(response);
        Assert.Equal(JobStatus.Assigned, details.Status);
        Assert.Equal(new JobTechnicianReference(second.UserId, second.User.DisplayName, Trade.Hvac), details.AssignedTechnician);
        Assert.Equal(Now, details.AssignedAt);
        Assert.NotEqual(job.Version, details.Version);
        Assert.Equal(
            [JobHistoryAction.Created, JobHistoryAction.Assigned, JobHistoryAction.Assigned],
            details.History.Select(entry => entry.Action));
        Assert.Equal(
            [null, new UserReference(first.UserId, first.User.DisplayName), new UserReference(second.UserId, second.User.DisplayName)],
            details.History.Select(entry => entry.Technician));
        Assert.Equal(JobStatus.Assigned, details.History[2].FromStatus);
        var recorded = Fixture.Factory.Events.SingleOf(JobEventTypes.Assigned, job.Id);
        Assert.Single(Fixture.Factory.Events.Events);
        Assert.Equal(JobStatus.Assigned, recorded.Status);
        Assert.Equal(JobStatus.Assigned, recorded.PreviousStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssigningTheSameTechnicianAgainReturnsTheJobUnchangedWithNoHistoryEntryAndNoEventWhateverTheVersion(bool staleVersion)
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var technician = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var created = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        var job = await DataAt(Earlier.AddMinutes(5)).AssignJobAsync(created, technician, CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.PutJsonAsync(
            PathOf(job.Id),
            Body(technician.UserId, staleVersion ? created.Version : job.Version));

        var details = await ReadJobAsync(response);
        Assert.Equal(JobStatus.Assigned, details.Status);
        Assert.Equal(technician.UserId, details.AssignedTechnician?.Id);
        Assert.Equal(job.Version, details.Version);
        Assert.Equal(Earlier.AddMinutes(5), details.UpdatedAt);
        Assert.Equal(Earlier.AddMinutes(5), details.AssignedAt);
        Assert.Equal(2, details.History.Count);
        await AssertUnchangedAsync(job);
    }

    [Fact]
    public async Task AStaleVersionReturns409ConcurrencyConflictAndWritesNothing()
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var first = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var second = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var created = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        var job = await DataAt(Earlier.AddMinutes(5)).AssignJobAsync(created, first, CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(second.UserId, created.Version));

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Conflict, "concurrency.conflict");
        Assert.Equal("The job was changed by someone else.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.10", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(client.LastCorrelationId, problem.RootElement.GetProperty("correlationId").GetString());
        await AssertUnchangedAsync(job);
    }

    [Fact]
    public async Task AVersionTheJobNeverHadReturns409ConcurrencyConflictForAnUnassignedJob()
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var technician = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var job = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(technician.UserId, unchecked(job.Version + 1)));

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.Conflict, "concurrency.conflict");
        await AssertUnchangedAsync(job);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task AnOffShiftOrInactiveTechnicianReturns409TechnicianUnavailableAndWritesNothing(bool isOnShift, bool isActive)
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var technician = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken, isOnShift, isActive);
        var job = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(technician.UserId, job.Version));

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Conflict, "technician.unavailable");
        var root = problem.RootElement;
        Assert.Equal("The technician is not available.", root.GetProperty("title").GetString());
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.10", root.GetProperty("type").GetString());
        Assert.Equal("The technician is inactive or off shift.", root.GetProperty("detail").GetString());
        Assert.False(root.TryGetProperty("errors", out _));
        await AssertUnchangedAsync(job);
    }

    [Theory]
    [InlineData(JobStatus.InProgress)]
    [InlineData(JobStatus.OnHold)]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Cancelled)]
    public async Task AJobThatIsNeitherUnassignedNorAssignedReturns409JobNotAssignableAndWritesNothing(JobStatus status)
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var assignee = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var other = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var created = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        var job = await DataAt(Earlier.AddMinutes(5)).MoveToAsync(created, assignee, status, CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(other.UserId, job.Version));

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Conflict, "job.not-assignable");
        var root = problem.RootElement;
        Assert.Equal("The job can no longer be assigned.", root.GetProperty("title").GetString());
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.10", root.GetProperty("type").GetString());
        Assert.Equal($"JOB-{job.Number} is {status} and can no longer be assigned.", root.GetProperty("detail").GetString());
        await AssertUnchangedAsync(job);
    }

    [Theory]
    [InlineData(JobStatus.InProgress)]
    [InlineData(JobStatus.Cancelled)]
    public async Task TheCurrentAssigneeOfAJobPastAssignedIsNotTheSameStateAndGets409JobNotAssignable(JobStatus status)
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var assignee = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var created = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        var job = await DataAt(Earlier.AddMinutes(5)).MoveToAsync(created, assignee, status, CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(assignee.UserId, job.Version));

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.Conflict, "job.not-assignable");
        await AssertUnchangedAsync(job);
    }

    [Fact]
    public async Task ABrokenRuleWinsOverAStaleVersion()
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var assignee = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var offShift = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken, isOnShift: false);
        var unassigned = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        var created = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        var completed = await DataAt(Earlier.AddMinutes(5)).MoveToAsync(created, assignee, JobStatus.Completed, CancellationToken);
        using var client = ClientFor(administrator);

        using var notAssignable = await client.PutJsonAsync(PathOf(completed.Id), Body(assignee.UserId, created.Version));
        using var unavailable = await client.PutJsonAsync(PathOf(unassigned.Id), Body(offShift.UserId, unchecked(unassigned.Version + 1)));

        await ApiClient.AssertProblemAsync(notAssignable, HttpStatusCode.Conflict, "job.not-assignable");
        await ApiClient.AssertProblemAsync(unavailable, HttpStatusCode.Conflict, "technician.unavailable");
        await AssertUnchangedAsync(completed);
        await AssertUnchangedAsync(unassigned);
    }

    [Fact]
    public async Task AnUnknownTechnicianOrAUserWhoIsNotATechnicianReturns400KeyedTechnicianId()
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var job = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        using var client = ClientFor(administrator);

        foreach (var technicianId in new[] { Guid.CreateVersion7(), administrator.Id, Guid.Empty })
        {
            using var response = await client.PutJsonAsync(PathOf(job.Id), Body(technicianId, job.Version));

            await ApiClient.AssertFieldErrorsAsync(response, "technicianId");
            Assert.Equal([AssigneeQuery.UnknownMessage], await MessagesAsync(response, "technicianId"));
        }

        await AssertUnchangedAsync(job);
    }

    [Fact]
    public async Task AnUnknownTechnicianIsReportedBeforeTheSameStateCheckAndTheRules()
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var assignee = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var created = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        var job = await DataAt(Earlier.AddMinutes(5)).MoveToAsync(created, assignee, JobStatus.Completed, CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(Guid.CreateVersion7(), created.Version));

        await ApiClient.AssertFieldErrorsAsync(response, "technicianId");
        await AssertUnchangedAsync(job);
    }

    [Theory]
    [InlineData("{}", "technicianId,version")]
    [InlineData("{\"technicianId\":\"@technician\"}", "version")]
    [InlineData("{\"version\":1}", "technicianId")]
    [InlineData("{\"technicianId\":null,\"version\":1}", "technicianId")]
    [InlineData("{\"technicianId\":\"@technician\",\"version\":null}", "version")]
    [InlineData("{\"technicianId\":\"@technician\",\"version\":\"48213\"}", "version")]
    [InlineData("{\"technicianId\":\"@technician\",\"version\":-1}", "version")]
    [InlineData("{\"technicianId\":\"@technician\",\"version\":4294967296}", "version")]
    [InlineData("{\"technicianId\":\"@technician\",\"version\":1.5}", "version")]
    [InlineData("{\"technicianId\":\"not-a-uuid\",\"version\":1}", "technicianId")]
    [InlineData("{\"technicianId\":42,\"version\":1}", "technicianId")]
    [InlineData("[]", "body")]
    [InlineData("not json", "body")]
    public async Task AMissingOrMalformedMemberReturnsAFieldErrorKeyedByItsPath(string body, string keys)
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var technician = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var job = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.PutAsync(
            PathOf(job.Id),
            new StringContent(body.Replace("@technician", technician.UserId.ToString(), StringComparison.Ordinal), Encoding.UTF8, "application/json"));

        await ApiClient.AssertFieldErrorsAsync(response, keys.Split(','));
        await AssertUnchangedAsync(job);
    }

    [Fact]
    public async Task AMissingBodyGets400AndAnotherMediaTypeGets415()
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var technician = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var job = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        using var client = ClientFor(administrator);

        using var missing = await client.PutAsync(PathOf(job.Id));
        using var text = await client.PutAsync(
            PathOf(job.Id),
            new StringContent($"{{\"technicianId\":\"{technician.UserId}\",\"version\":{job.Version}}}", Encoding.UTF8, "text/plain"));

        await ApiClient.AssertFieldErrorsAsync(missing, "body");
        await ApiClient.AssertProblemAsync(text, HttpStatusCode.UnsupportedMediaType, "request.unsupported-media-type");
        await AssertUnchangedAsync(job);
    }

    [Fact]
    public async Task AnUnknownJobReturns404JobNotFoundWrittenAsProblemJson()
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var technician = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.PutJsonAsync(PathOf(Guid.CreateVersion7()), Body(technician.UserId, 1));

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.NotFound, "job.not-found");
        Assert.Equal("The job was not found.", problem.RootElement.GetProperty("title").GetString());
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task AnUnknownJobIsReportedBeforeAnUnknownTechnician()
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.PutJsonAsync(PathOf(Guid.CreateVersion7()), Body(Guid.CreateVersion7(), 1));

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.NotFound, "job.not-found");
    }

    [Fact]
    public async Task AJobIdThatIsNotAGuidMatchesNoRoute()
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var technician = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.PutJsonAsync("/api/jobs/1042/assignment", Body(technician.UserId, 1));

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.NotFound, "resource.not-found");
    }

    [Fact]
    public async Task AnAnonymousCallerGets401AndNothingIsWritten()
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var technician = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var job = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(technician.UserId, job.Version));

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        await AssertUnchangedAsync(job);
    }

    [Fact]
    public async Task ATechnicianGets403EvenForTheirOwnJobAndNothingIsWritten()
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var assignee = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var other = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var created = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        var job = await DataAt(Earlier.AddMinutes(5)).AssignJobAsync(created, assignee, CancellationToken);
        using var client = ClientFor(assignee.User);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(other.UserId, job.Version));
        using var invalid = await client.PutJsonAsync(PathOf(job.Id), new { });

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.Forbidden, "auth.forbidden");
        await ApiClient.AssertProblemAsync(invalid, HttpStatusCode.Forbidden, "auth.forbidden");
        await AssertUnchangedAsync(job);
    }

    [Fact]
    public async Task TheNewAssigneeSeesTheJobInTheirListAndReadsItWithoutTheAssignAffordance()
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var first = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var second = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var created = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        var job = await DataAt(Earlier.AddMinutes(5)).AssignJobAsync(created, first, CancellationToken);
        using var administratorClient = ClientFor(administrator);
        using var firstClient = ClientFor(first.User);
        using var secondClient = ClientFor(second.User);

        using var assign = await administratorClient.PutJsonAsync(PathOf(job.Id), Body(second.UserId, job.Version));
        using var secondList = await secondClient.GetAsync("/api/technicians/me/jobs");
        using var firstList = await firstClient.GetAsync("/api/technicians/me/jobs");
        using var secondDetail = await secondClient.GetAsync($"/api/jobs/{job.Id}");
        using var firstDetail = await firstClient.GetAsync($"/api/jobs/{job.Id}");

        var assigned = await ReadJobAsync(assign);
        var secondItems = (await secondList.Content.ReadFromJsonAsync<GetAssignedJobsResponse>(ApiClient.Json, CancellationToken))!.Items;
        Assert.Equal([job.Id], secondItems.Select(item => item.Id));
        Assert.Equal(assigned.Version, secondItems[0].Version);
        Assert.Empty((await firstList.Content.ReadFromJsonAsync<GetAssignedJobsResponse>(ApiClient.Json, CancellationToken))!.Items);
        var detail = await ReadJobAsync(secondDetail);
        Assert.False(detail.Affordances.CanAssign);
        await ApiClient.AssertProblemAsync(firstDetail, HttpStatusCode.NotFound, "job.not-found");
    }

    [Fact]
    public async Task AssigningLogsNoNameEmailOrPhoneAtInformationLevel()
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var technician = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var job = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        await using var factory = Fixture.CreateFactory(factory =>
        {
            factory.Settings["Serilog:MinimumLevel:Default"] = "Information";
            factory.Settings["Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore"] = "Information";
        });
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, administrator);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(technician.UserId, job.Version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(factory.LogSink.Events);
        foreach (var text in new[]
        {
            technician.User.DisplayName, technician.User.Email, technician.PhoneNumber, administrator.DisplayName,
            administrator.Email, job.Title, job.CustomerName, job.SiteAddress,
        })
        {
            Assert.All(factory.LogSink.Events, logEvent =>
                Assert.DoesNotContain(text, CapturingLogSink.Text(logEvent), StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task AfterTheDemoSeedDanaAssignsJob1056ToAishaKarimAndSamuelOseiIsUnavailable()
    {
        await RestartJobNumbersAsync();
        var factory = Fixture.CreateFactory(factory =>
        {
            factory.EnvironmentName = "Development";
            factory.Settings["Logs:FileEnabled"] = "false";
            factory.Settings["Seed:DemoData"] = "true";
            factory.Settings["Seed:DemoPassword"] = $"demo-{Guid.NewGuid():N}";
        });
        try
        {
            _ = factory.Services;
            await using var db = Fixture.CreateAppDbContext();
            var dana = await db.Users.AsNoTracking().SingleAsync(user => user.Role == UserRole.Administrator, CancellationToken);
            var aisha = await db.Users.AsNoTracking().SingleAsync(user => user.Email == "aisha.karim@fieldservice.example", CancellationToken);
            var samuel = await db.Users.AsNoTracking().SingleAsync(user => user.Email == "samuel.osei@fieldservice.example", CancellationToken);
            using var client = factory.CreateApiClient();
            client.AccessToken = TestTokens.For(factory, dana);
            using var create = await client.PostJsonAsync("/api/jobs", new
            {
                title = "Exterior signage lights out",
                description = "Both pylon signs at the north entrance are dark after 6 PM.",
                priority = "Normal",
                customerName = "Riverside Shopping Center",
                siteAddress = "500 Riverside Boulevard",
                scheduledStart = Now.AddDays(1),
                scheduledEnd = Now.AddDays(1).AddHours(2),
                checklistTemplateId = _templateId,
            });
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            var created = (await create.Content.ReadFromJsonAsync<JobDetailsResponse>(ApiClient.Json, CancellationToken))!;
            Assert.Equal(1056, created.Number);

            using var offShift = await client.PutJsonAsync(PathOf(created.Id), Body(samuel.Id, created.Version));
            using var assign = await client.PutJsonAsync(PathOf(created.Id), Body(aisha.Id, created.Version));

            await ApiClient.AssertProblemAsync(offShift, HttpStatusCode.Conflict, "technician.unavailable");
            var assigned = await ReadJobAsync(assign);
            Assert.Equal(JobStatus.Assigned, assigned.Status);
            Assert.Equal(new JobTechnicianReference(aisha.Id, "Aisha Karim", Trade.Electrical), assigned.AssignedTechnician);
            Assert.NotNull(assigned.AssignedAt);
            Assert.NotEqual(created.Version, assigned.Version);
            var entry = assigned.History[^1];
            Assert.Equal(JobHistoryAction.Assigned, entry.Action);
            Assert.Equal(new UserReference(aisha.Id, "Aisha Karim"), entry.Technician);
            Assert.Equal(new UserReference(dana.Id, "Dana Whitfield"), entry.Actor);
            var recorded = factory.Events.SingleOf(JobEventTypes.Assigned, created.Id);
            Assert.Equal(1056, recorded.JobNumber);
            Assert.Equal(JobStatus.Unassigned, recorded.PreviousStatus);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task ACommittedAssignmentIsPostedOnceAsASignedJobAssignedEnvelopeWithTheRequestsCorrelationId()
    {
        NotificationsStub? stub = null;
        await using var factory = Fixture.CreateFactory(factory => stub = NotificationsStub.AttachTo(factory));
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var technician = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var job = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, administrator);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(technician.UserId, job.Version));
        var details = await ReadJobAsync(response);
        await stub!.WaitForRequestsAsync(1);

        var webhook = Assert.Single(stub.Requests);
        NotificationsStub.AssertSignedEnvelope(
            factory,
            webhook,
            client.LastCorrelationId!,
            JobEventTypes.Assigned,
            job.Id,
            details.Number,
            JobStatus.Assigned,
            JobStatus.Unassigned);
    }

    public static object Body(Guid technicianId, uint version) => new { technicianId, version };

    public static string PathOf(Guid jobId) => $"/api/jobs/{jobId}/assignment";

    private static async Task<JobDetailsResponse> ReadJobAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var job = await response.Content.ReadFromJsonAsync<JobDetailsResponse>(ApiClient.Json, TestContext.Current.CancellationToken);
        return job ?? throw new InvalidOperationException("The response has no job body.");
    }

    private static async Task<IReadOnlyList<string?>> MessagesAsync(HttpResponseMessage response, string key)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return [.. problem.RootElement.GetProperty("errors").GetProperty(key).EnumerateArray().Select(message => message.GetString())];
    }

    private TestData DataAt(DateTimeOffset now) => new(Fixture, now);

    private ApiClient ClientFor(User user)
    {
        var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TestTokens.For(Fixture.Factory, user);
        return client;
    }

    private async Task AssertUnchangedAsync(Job job)
    {
        var requestInstant = Now;
        await using var db = Fixture.CreateAppDbContext();
        var stored = await db.Jobs.AsNoTracking().SingleAsync(entry => entry.Id == job.Id, CancellationToken);
        Assert.Equal(job.Version, stored.Version);
        Assert.Equal(job.Status, stored.Status);
        Assert.Equal(job.AssignedTechnicianId, stored.AssignedTechnicianId);
        Assert.Equal(job.AssignedAt, stored.AssignedAt);
        Assert.Equal(job.UpdatedAt, stored.UpdatedAt);
        Assert.True(stored.UpdatedAt < requestInstant);
        Assert.Equal(
            0,
            await db.JobHistory.CountAsync(
                entry => entry.JobId == job.Id && entry.OccurredAt >= requestInstant,
                CancellationToken));
        Fixture.Factory.Events.AssertEmpty();
    }

    private async Task RestartJobNumbersAsync()
    {
        await using var migrator = await Fixture.OpenMigratorConnectionAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT setval('fieldservice.jobs_number_seq', 1001, false)", migrator);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }
}
