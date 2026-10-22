using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Features;
using FieldService.Api.Features.Jobs;
using FieldService.Api.Features.Jobs.UpdateJobStatus;
using FieldService.Api.Persistence;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Shared.Errors;
using FieldService.Api.Shared.Realtime;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FieldService.Api.Tests.Features.Jobs;

[Collection(DatabaseCollection.Name)]
public sealed class UpdateJobStatusTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string Same = "same state";

    private const string Invalid = "job.invalid-transition";

    private const string HoldNoteMessage = "Enter a note of at most 500 characters to put the job on hold.";

    private static readonly Guid _templateId = ChecklistTemplateSeed.TemplateId(1);

    private DateTimeOffset Now => UtcInstant.Truncate(Fixture.Factory.Clock.GetUtcNow());

    private DateTimeOffset Earlier => Now.AddMinutes(-30);

    [Fact]
    public async Task StartingAnAssignedJobMovesItToInProgressSetsStartedAtAndAddsAStartedEntry()
    {
        var (_, technician, job) = await ArrangeAsync(JobStatus.Assigned);
        using var client = ClientFor(technician.User);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(JobStatus.InProgress, null, job.Version));

        var details = await ReadJobAsync(response);
        Assert.Equal(JobStatus.InProgress, details.Status);
        Assert.Equal(Now, details.StartedAt);
        Assert.Equal(Now, details.UpdatedAt);
        Assert.Equal(job.AssignedAt, details.AssignedAt);
        Assert.NotEqual(job.Version, details.Version);
        Assert.Equal(
            [JobHistoryAction.Created, JobHistoryAction.Assigned, JobHistoryAction.Started],
            details.History.Select(entry => entry.Action));
        var started = details.History[^1];
        Assert.Equal(JobHistoryAction.Started, started.Action);
        Assert.Equal(JobStatus.Assigned, started.FromStatus);
        Assert.Equal(JobStatus.InProgress, started.ToStatus);
        Assert.Equal(new UserReference(technician.UserId, technician.User.DisplayName), started.Actor);
        Assert.Null(started.Technician);
        Assert.Null(started.Note);
        Assert.Equal(Now, started.OccurredAt);
        Assert.Equal([new AllowedStatusChange(JobStatus.OnHold, RequiresNote: true)], details.Affordances.AllowedStatusChanges);
        Assert.False(details.Affordances.CanAssign);
    }

    [Fact]
    public async Task PuttingAnInProgressJobOnHoldStoresTheTrimmedNoteOnAPutOnHoldEntryAndKeepsStartedAt()
    {
        var (_, technician, job) = await ArrangeAsync(JobStatus.InProgress);
        using var client = ClientFor(technician.User);

        using var response = await client.PutJsonAsync(
            PathOf(job.Id),
            Body(JobStatus.OnHold, "  Waiting for a replacement gas valve \n", job.Version));

        var details = await ReadJobAsync(response);
        Assert.Equal(JobStatus.OnHold, details.Status);
        Assert.Equal(job.StartedAt, details.StartedAt);
        Assert.Equal(Now, details.UpdatedAt);
        var onHold = details.History[^1];
        Assert.Equal(JobHistoryAction.PutOnHold, onHold.Action);
        Assert.Equal(JobStatus.InProgress, onHold.FromStatus);
        Assert.Equal(JobStatus.OnHold, onHold.ToStatus);
        Assert.Equal("Waiting for a replacement gas valve", onHold.Note);
        Assert.Equal(Now, onHold.OccurredAt);
        Assert.Equal([new AllowedStatusChange(JobStatus.InProgress, RequiresNote: false)], details.Affordances.AllowedStatusChanges);
    }

    [Fact]
    public async Task ResumingAnOnHoldJobAddsAResumedEntryWithoutANoteAndKeepsStartedAt()
    {
        var (_, technician, job) = await ArrangeAsync(JobStatus.OnHold);
        using var client = ClientFor(technician.User);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(JobStatus.InProgress, "Part arrived", job.Version));

        var details = await ReadJobAsync(response);
        Assert.Equal(JobStatus.InProgress, details.Status);
        Assert.Equal(job.StartedAt, details.StartedAt);
        var resumed = details.History[^1];
        Assert.Equal(JobHistoryAction.Resumed, resumed.Action);
        Assert.Equal(JobStatus.OnHold, resumed.FromStatus);
        Assert.Equal(JobStatus.InProgress, resumed.ToStatus);
        Assert.Null(resumed.Note);
        Assert.Equal(Now, resumed.OccurredAt);
        Assert.Equal([new AllowedStatusChange(JobStatus.OnHold, RequiresNote: true)], details.Affordances.AllowedStatusChanges);
    }

    [Fact]
    public async Task StartedAtIsSetOnTheFirstStartOnlyAcrossStartHoldAndResume()
    {
        await using var factory = Fixture.CreateFactory();
        var data = new TestData(Fixture, UtcInstant.Truncate(factory.Clock.GetUtcNow()).AddMinutes(-30));
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var technician = await data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var job = await data.MoveToAsync(
            await data.CreateJobAsync(administrator.Id, _templateId, CancellationToken),
            technician,
            JobStatus.Assigned,
            CancellationToken);
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, technician.User);
        var firstStart = UtcInstant.Truncate(factory.Clock.GetUtcNow());

        using var start = await client.PutJsonAsync(PathOf(job.Id), Body(JobStatus.InProgress, null, job.Version));
        var started = await ReadJobAsync(start);
        factory.Clock.Advance(TimeSpan.FromMinutes(20));
        client.AccessToken = TestTokens.For(factory, technician.User);
        using var hold = await client.PutJsonAsync(PathOf(job.Id), Body(JobStatus.OnHold, TestData.HoldNote, started.Version));
        var onHold = await ReadJobAsync(hold);
        factory.Clock.Advance(TimeSpan.FromMinutes(25));
        client.AccessToken = TestTokens.For(factory, technician.User);
        using var resume = await client.PutJsonAsync(PathOf(job.Id), Body(JobStatus.InProgress, null, onHold.Version));
        var resumed = await ReadJobAsync(resume);

        Assert.Equal(firstStart, started.StartedAt);
        Assert.Equal(firstStart, onHold.StartedAt);
        Assert.Equal(firstStart, resumed.StartedAt);
        Assert.Equal(firstStart.AddMinutes(45), resumed.UpdatedAt);
        Assert.Equal(
            [JobHistoryAction.Started, JobHistoryAction.PutOnHold, JobHistoryAction.Resumed],
            resumed.History.TakeLast(3).Select(entry => entry.Action));
        Assert.Equal(
            [firstStart, firstStart.AddMinutes(20), firstStart.AddMinutes(45)],
            resumed.History.TakeLast(3).Select(entry => entry.OccurredAt));
        Assert.Equal(
            [(JobStatus.InProgress, JobStatus.Assigned), (JobStatus.OnHold, JobStatus.InProgress), (JobStatus.InProgress, JobStatus.OnHold)],
            factory.Events.Events.Select(jobEvent => (jobEvent.Status, jobEvent.PreviousStatus!.Value)));
        await using var db = Fixture.CreateAppDbContext();
        var stored = await db.Jobs.AsNoTracking().SingleAsync(entry => entry.Id == job.Id, CancellationToken);
        Assert.Equal(firstStart, stored.StartedAt);
    }

    [Fact]
    public async Task TheStatusChangeReadsBackThroughAFreshDbContextWithTheReturnedVersion()
    {
        var (_, technician, job) = await ArrangeAsync(JobStatus.InProgress);
        using var client = ClientFor(technician.User);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(JobStatus.OnHold, TestData.HoldNote, job.Version));

        var details = await ReadJobAsync(response);
        await using var db = Fixture.CreateAppDbContext();
        var stored = await db.Jobs.AsNoTracking().Include(entry => entry.History).SingleAsync(entry => entry.Id == job.Id, CancellationToken);
        Assert.Equal(JobStatus.OnHold, stored.Status);
        Assert.Equal(job.StartedAt, stored.StartedAt);
        Assert.Equal(Now, stored.UpdatedAt);
        Assert.Equal(details.Version, stored.Version);
        var entry = Assert.Single(stored.History, entry => entry.Action == JobHistoryAction.PutOnHold);
        Assert.Equal(details.History[^1].Id, entry.Id);
        Assert.Equal(technician.UserId, entry.ActorUserId);
        Assert.Null(entry.TechnicianId);
        Assert.Equal(JobStatus.InProgress, entry.FromStatus);
        Assert.Equal(JobStatus.OnHold, entry.ToStatus);
        Assert.Equal(TestData.HoldNote, entry.Note);
        Assert.Equal(Now, entry.OccurredAt);
    }

    [Fact]
    public async Task OneJobStatusChangedEventIsRecordedOnlyAfterTheCommit()
    {
        var (_, technician, job) = await ArrangeAsync(JobStatus.Assigned);
        using var client = ClientFor(technician.User);
        bool? committedWhenEnqueued = null;
        Fixture.Factory.Events.OnEnqueue = jobEvent =>
        {
            using var db = Fixture.CreateAppDbContext();
            committedWhenEnqueued = db.Jobs.AsNoTracking()
                .Any(entry => entry.Id == jobEvent.JobId && entry.Status == JobStatus.InProgress);
        };

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(JobStatus.InProgress, null, job.Version));

        var details = await ReadJobAsync(response);
        var recorded = Assert.Single(Fixture.Factory.Events.Events);
        Assert.True(committedWhenEnqueued);
        Assert.Equal(JobEventTypes.StatusChanged, recorded.Type);
        Assert.Equal(JobEvent.CurrentVersion, recorded.Version);
        Assert.Equal(job.Id, recorded.JobId);
        Assert.Equal(job.Number, recorded.JobNumber);
        Assert.Equal(JobStatus.InProgress, recorded.Status);
        Assert.Equal(JobStatus.Assigned, recorded.PreviousStatus);
        Assert.Equal(details.UpdatedAt, recorded.OccurredAt);
        Assert.Equal(client.LastCorrelationId, recorded.CorrelationId);
    }

    [Fact]
    public async Task ARolledBackSaveReturns500WritesNothingAndRecordsNoEvent()
    {
        var refusal = new CommitRefusal();
        await using var factory = Fixture.CreateFactory(factory => factory.TestServices = services =>
            services.ConfigureDbContext<FieldServiceDbContext>(options => options.AddInterceptors(refusal)));
        var (_, technician, job) = await ArrangeAsync(JobStatus.Assigned);
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, technician.User);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(JobStatus.InProgress, null, job.Version));

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.InternalServerError, "server.error");
        Assert.Equal(1, refusal.Attempts);
        await AssertUnchangedAsync(job, factory.Events);
    }

    [Theory]
    [InlineData(JobStatus.InProgress, null, false)]
    [InlineData(JobStatus.InProgress, "On site now", true)]
    [InlineData(JobStatus.OnHold, "Another reason", false)]
    [InlineData(JobStatus.OnHold, null, true)]
    public async Task ResendingTheCurrentStatusReturnsTheJobUnchangedWithNoHistoryEntryAndNoEventWhateverTheVersionOrNote(
        JobStatus status,
        string? note,
        bool staleVersion)
    {
        var (created, technician, job) = await ArrangeAsync(status);
        var historyCount = await HistoryCountAsync(job.Id);
        using var client = ClientFor(technician.User);

        using var response = await client.PutJsonAsync(
            PathOf(job.Id),
            Body(status, note, staleVersion ? created.Version : job.Version));

        var details = await ReadJobAsync(response);
        Assert.Equal(status, details.Status);
        Assert.Equal(job.Version, details.Version);
        Assert.Equal(job.UpdatedAt, details.UpdatedAt);
        Assert.Equal(historyCount, details.History.Count);
        Assert.Equal(
            status == JobStatus.OnHold ? [TestData.HoldNote] : [],
            details.History.Where(entry => entry.Note is not null).Select(entry => entry.Note));
        await AssertUnchangedAsync(job);
    }

    [Theory]
    [InlineData(JobStatus.Assigned, JobStatus.InProgress, "Started")]
    [InlineData(JobStatus.Assigned, JobStatus.OnHold, Invalid)]
    [InlineData(JobStatus.InProgress, JobStatus.InProgress, Same)]
    [InlineData(JobStatus.InProgress, JobStatus.OnHold, "PutOnHold")]
    [InlineData(JobStatus.OnHold, JobStatus.InProgress, "Resumed")]
    [InlineData(JobStatus.OnHold, JobStatus.OnHold, Same)]
    [InlineData(JobStatus.Completed, JobStatus.InProgress, Invalid)]
    [InlineData(JobStatus.Completed, JobStatus.OnHold, Invalid)]
    [InlineData(JobStatus.Cancelled, JobStatus.InProgress, Invalid)]
    [InlineData(JobStatus.Cancelled, JobStatus.OnHold, Invalid)]
    public async Task EveryStatusChangeTheAssigneeCanRequestFollowsTheTransitionTable(
        JobStatus from,
        JobStatus to,
        string expected)
    {
        var (_, technician, job) = await ArrangeAsync(from);
        using var client = ClientFor(technician.User);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(to, TestData.HoldNote, job.Version));

        if (expected == Invalid)
        {
            using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Conflict, Invalid);
            var root = problem.RootElement;
            Assert.Equal("The job cannot move to that status.", root.GetProperty("title").GetString());
            Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.10", root.GetProperty("type").GetString());
            Assert.Equal($"JOB-{job.Number} cannot move from {from} to {to}.", root.GetProperty("detail").GetString());
            Assert.False(root.TryGetProperty("errors", out _));
            await AssertUnchangedAsync(job);
            return;
        }

        var details = await ReadJobAsync(response);
        Assert.Equal(to, details.Status);
        if (expected == Same)
        {
            Assert.Equal(job.Version, details.Version);
            await AssertUnchangedAsync(job);
            return;
        }

        var entry = details.History[^1];
        Assert.Equal(expected, entry.Action.ToString());
        Assert.Equal(from, entry.FromStatus);
        Assert.Equal(to, entry.ToStatus);
        Assert.Equal(Now, entry.OccurredAt);
        var recorded = Fixture.Factory.Events.SingleOf(JobEventTypes.StatusChanged, job.Id);
        Assert.Single(Fixture.Factory.Events.Events);
        Assert.Equal(to, recorded.Status);
        Assert.Equal(from, recorded.PreviousStatus);
    }

    [Fact]
    public async Task ABrokenTransitionOrAMissingHoldNoteWinsOverAStaleVersion()
    {
        var (createdCompleted, completedAssignee, completed) = await ArrangeAsync(JobStatus.Completed);
        var (createdAssigned, assignedAssignee, assigned) = await ArrangeAsync(JobStatus.Assigned);
        var (createdInProgress, inProgressAssignee, inProgress) = await ArrangeAsync(JobStatus.InProgress);
        using var completedClient = ClientFor(completedAssignee.User);
        using var assignedClient = ClientFor(assignedAssignee.User);
        using var inProgressClient = ClientFor(inProgressAssignee.User);

        using var fromCompleted = await completedClient.PutJsonAsync(
            PathOf(completed.Id),
            Body(JobStatus.InProgress, null, createdCompleted.Version));
        using var holdFromAssigned = await assignedClient.PutJsonAsync(
            PathOf(assigned.Id),
            Body(JobStatus.OnHold, TestData.HoldNote, unchecked(createdAssigned.Version + 1)));
        using var holdWithoutNote = await inProgressClient.PutJsonAsync(
            PathOf(inProgress.Id),
            Body(JobStatus.OnHold, null, createdInProgress.Version));

        await ApiClient.AssertProblemAsync(fromCompleted, HttpStatusCode.Conflict, Invalid);
        await ApiClient.AssertProblemAsync(holdFromAssigned, HttpStatusCode.Conflict, Invalid);
        await ApiClient.AssertFieldErrorsAsync(holdWithoutNote, "note");
        await AssertUnchangedAsync(completed);
        await AssertUnchangedAsync(assigned);
        await AssertUnchangedAsync(inProgress);
    }

    [Fact]
    public async Task AStaleVersionReturns409ConcurrencyConflictAndWritesNothing()
    {
        var (created, technician, job) = await ArrangeAsync(JobStatus.InProgress);
        using var client = ClientFor(technician.User);

        using var stale = await client.PutJsonAsync(PathOf(job.Id), Body(JobStatus.OnHold, TestData.HoldNote, created.Version));
        using var never = await client.PutJsonAsync(PathOf(job.Id), Body(JobStatus.OnHold, TestData.HoldNote, unchecked(job.Version + 1)));

        using var problem = await ApiClient.AssertProblemAsync(stale, HttpStatusCode.Conflict, "concurrency.conflict");
        Assert.Equal("The job was changed by someone else.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.10", problem.RootElement.GetProperty("type").GetString());
        await ApiClient.AssertProblemAsync(never, HttpStatusCode.Conflict, "concurrency.conflict");
        await AssertUnchangedAsync(job);
    }

    [Theory]
    [InlineData("", HoldNoteMessage)]
    [InlineData(",\"note\":null", HoldNoteMessage)]
    [InlineData(",\"note\":\"\"", HoldNoteMessage)]
    [InlineData(",\"note\":\"   \"", HoldNoteMessage)]
    [InlineData(",\"note\":\"\\n\\t\"", HoldNoteMessage)]
    [InlineData(",\"note\":\"@long\"", "Enter at most 500 characters.")]
    [InlineData(",\"note\":\"Waiting\\u0000\"", MultiLineAttribute.Message)]
    [InlineData(",\"note\":\"Waiting\\u0007\"", MultiLineAttribute.Message)]
    public async Task PuttingAJobOnHoldWithoutAUsableNoteReturns400KeyedNoteAndWritesNothing(string note, string message)
    {
        var (_, technician, job) = await ArrangeAsync(JobStatus.InProgress);
        using var client = ClientFor(technician.User);
        var body = $"{{\"status\":\"OnHold\",\"version\":{job.Version}{note.Replace("@long", new string('x', 501), StringComparison.Ordinal)}}}";

        using var response = await client.PutAsync(PathOf(job.Id), new StringContent(body, Encoding.UTF8, "application/json"));

        await ApiClient.AssertFieldErrorsAsync(response, "note");
        Assert.Equal([message], await MessagesAsync(response, "note"));
        await AssertUnchangedAsync(job);
    }

    [Fact]
    public async Task AHoldNoteOfExactlyFiveHundredCharactersIsStored()
    {
        var (_, technician, job) = await ArrangeAsync(JobStatus.InProgress);
        using var client = ClientFor(technician.User);
        var note = new string('n', JobTransitions.MaxNoteLength);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(JobStatus.OnHold, note, job.Version));

        var details = await ReadJobAsync(response);
        Assert.Equal(note, details.History[^1].Note);
    }

    [Theory]
    [InlineData(JobStatus.Unassigned)]
    [InlineData(JobStatus.Assigned)]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Cancelled)]
    public async Task ATargetOtherThanInProgressOrOnHoldReturns400KeyedStatusAndWritesNothing(JobStatus target)
    {
        var (_, technician, job) = await ArrangeAsync(JobStatus.InProgress);
        using var client = ClientFor(technician.User);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(target, TestData.HoldNote, job.Version));

        await ApiClient.AssertFieldErrorsAsync(response, "status");
        Assert.Equal([UpdateJobStatusRequest.StatusMessage], await MessagesAsync(response, "status"));
        await AssertUnchangedAsync(job);
    }

    [Theory]
    [InlineData("{}", "status,version")]
    [InlineData("{\"status\":\"InProgress\"}", "version")]
    [InlineData("{\"version\":1}", "status")]
    [InlineData("{\"status\":null,\"version\":1}", "status")]
    [InlineData("{\"status\":\"Paused\",\"version\":1}", "status")]
    [InlineData("{\"status\":2,\"version\":1}", "status")]
    [InlineData("{\"status\":\"InProgress\",\"version\":null}", "version")]
    [InlineData("{\"status\":\"InProgress\",\"version\":\"48213\"}", "version")]
    [InlineData("{\"status\":\"InProgress\",\"version\":-1}", "version")]
    [InlineData("{\"status\":\"InProgress\",\"version\":4294967296}", "version")]
    [InlineData("{\"status\":\"InProgress\",\"version\":1.5}", "version")]
    [InlineData("{\"status\":\"OnHold\",\"note\":42,\"version\":1}", "note")]
    [InlineData("{\"status\":\"InProgress\",\"status\":\"OnHold\",\"version\":1}", "status")]
    [InlineData("[]", "body")]
    [InlineData("not json", "body")]
    public async Task AMissingOrMalformedMemberReturnsAFieldErrorKeyedByItsPath(string body, string keys)
    {
        var (_, technician, job) = await ArrangeAsync(JobStatus.Assigned);
        using var client = ClientFor(technician.User);

        using var response = await client.PutAsync(PathOf(job.Id), new StringContent(body, Encoding.UTF8, "application/json"));

        await ApiClient.AssertFieldErrorsAsync(response, keys.Split(','));
        await AssertUnchangedAsync(job);
    }

    [Fact]
    public async Task AMissingBodyGets400AndAnotherMediaTypeGets415()
    {
        var (_, technician, job) = await ArrangeAsync(JobStatus.Assigned);
        using var client = ClientFor(technician.User);

        using var missing = await client.PutAsync(PathOf(job.Id));
        using var text = await client.PutAsync(
            PathOf(job.Id),
            new StringContent($"{{\"status\":\"InProgress\",\"version\":{job.Version}}}", Encoding.UTF8, "text/plain"));

        await ApiClient.AssertFieldErrorsAsync(missing, "body");
        await ApiClient.AssertProblemAsync(text, HttpStatusCode.UnsupportedMediaType, "request.unsupported-media-type");
        await AssertUnchangedAsync(job);
    }

    [Fact]
    public async Task AnotherTechniciansJobAndAnUnassignedJobReturnTheSame404AsAMissingJobAndNothingIsWritten()
    {
        var (_, _, job) = await ArrangeAsync(JobStatus.Assigned);
        var (unassigned, _, _) = await ArrangeAsync(JobStatus.Unassigned);
        var other = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        using var client = ClientFor(other.User);

        using var forOther = await client.PutJsonAsync(PathOf(job.Id), Body(JobStatus.InProgress, null, job.Version));
        using var forUnassigned = await client.PutJsonAsync(PathOf(unassigned.Id), Body(JobStatus.InProgress, null, unassigned.Version));
        using var forMissing = await client.PutJsonAsync(PathOf(Guid.CreateVersion7()), Body(JobStatus.InProgress, null, job.Version));

        using var first = await ApiClient.AssertProblemAsync(forOther, HttpStatusCode.NotFound, "job.not-found");
        using var second = await ApiClient.AssertProblemAsync(forUnassigned, HttpStatusCode.NotFound, "job.not-found");
        using var third = await ApiClient.AssertProblemAsync(forMissing, HttpStatusCode.NotFound, "job.not-found");
        foreach (var problem in new[] { first, second })
        {
            Assert.Equal(
                third.RootElement.EnumerateObject().Select(member => member.Name),
                problem.RootElement.EnumerateObject().Select(member => member.Name));
            Assert.Equal(third.RootElement.GetProperty("title").GetString(), problem.RootElement.GetProperty("title").GetString());
            Assert.False(problem.RootElement.TryGetProperty("detail", out _));
        }

        await AssertUnchangedAsync(job);
        await AssertUnchangedAsync(unassigned);
    }

    [Fact]
    public async Task AnUnknownJobIsReportedBeforeABrokenTransition()
    {
        var technician = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        using var client = ClientFor(technician.User);

        using var response = await client.PutJsonAsync(PathOf(Guid.CreateVersion7()), Body(JobStatus.OnHold, null, 1));

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.NotFound, "job.not-found");
        Assert.Equal("The job was not found.", problem.RootElement.GetProperty("title").GetString());
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task AJobIdThatIsNotAGuidMatchesNoRoute()
    {
        var technician = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        using var client = ClientFor(technician.User);

        using var response = await client.PutJsonAsync("/api/jobs/1047/status", Body(JobStatus.InProgress, null, 1));

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.NotFound, "resource.not-found");
    }

    [Fact]
    public async Task AnAnonymousCallerAnExpiredTokenAndATokenSignedWithAnotherKeyGet401AndNothingIsWritten()
    {
        var (_, technician, job) = await ArrangeAsync(JobStatus.Assigned);
        await using var expiring = Fixture.CreateFactory();
        await using var foreign = Fixture.CreateFactory();
        using var anonymous = Fixture.Factory.CreateApiClient();
        using var expired = expiring.CreateApiClient();
        expired.AccessToken = TestTokens.For(expiring, technician.User);
        expiring.Clock.Advance(TimeSpan.FromMinutes(16));
        using var signedElsewhere = Fixture.Factory.CreateApiClient();
        signedElsewhere.AccessToken = TestTokens.For(foreign, technician.User);

        using var withoutToken = await anonymous.PutJsonAsync(PathOf(job.Id), Body(JobStatus.InProgress, null, job.Version));
        using var withExpired = await expired.PutJsonAsync(PathOf(job.Id), Body(JobStatus.InProgress, null, job.Version));
        using var withForeign = await signedElsewhere.PutJsonAsync(PathOf(job.Id), Body(JobStatus.InProgress, null, job.Version));

        await ApiClient.AssertProblemAsync(withoutToken, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        await ApiClient.AssertProblemAsync(withExpired, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        await ApiClient.AssertProblemAsync(withForeign, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        await AssertUnchangedAsync(job);
        expiring.Events.AssertEmpty();
    }

    [Fact]
    public async Task AnAdministratorGets403EvenWithAnInvalidBodyAndNothingIsWritten()
    {
        var (_, _, job) = await ArrangeAsync(JobStatus.Assigned);
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);

        using var valid = await client.PutJsonAsync(PathOf(job.Id), Body(JobStatus.InProgress, null, job.Version));
        using var invalid = await client.PutJsonAsync(PathOf(job.Id), new { });

        await ApiClient.AssertProblemAsync(valid, HttpStatusCode.Forbidden, "auth.forbidden");
        await ApiClient.AssertProblemAsync(invalid, HttpStatusCode.Forbidden, "auth.forbidden");
        await AssertUnchangedAsync(job);
    }

    [Fact]
    public async Task ChangingStatusLogsNoNoteNameEmailOrJobTextAtInformationLevel()
    {
        var (_, technician, job) = await ArrangeAsync(JobStatus.InProgress);
        const string Note = "Waiting for the compressor relay from Harbor Supply";
        await using var factory = Fixture.CreateFactory(factory =>
        {
            factory.Settings["Serilog:MinimumLevel:Default"] = "Information";
            factory.Settings["Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore"] = "Information";
        });
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, technician.User);

        using var response = await client.PutJsonAsync(PathOf(job.Id), Body(JobStatus.OnHold, Note, job.Version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(factory.LogSink.Events);
        foreach (var text in new[]
        {
            Note, "Harbor Supply", technician.User.DisplayName, technician.User.Email, technician.PhoneNumber,
            job.Title, job.CustomerName, job.SiteAddress,
        })
        {
            Assert.All(factory.LogSink.Events, logEvent =>
                Assert.DoesNotContain(text, CapturingLogSink.Text(logEvent), StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void TheAllowedStatusValuesAreExactlyTheStatusChangeTargetsOfTheTransitionTable()
    {
        var allowed = typeof(UpdateJobStatusRequest)
            .GetProperty(nameof(UpdateJobStatusRequest.Status))!
            .GetCustomAttribute<AllowedValuesAttribute>()!
            .Values
            .Cast<JobStatus>()
            .Order()
            .ToList();

        var targets = JobTransitions.All
            .Where(transition => transition.Command == JobCommand.ChangeStatus)
            .Select(transition => transition.To)
            .Distinct()
            .Order()
            .ToList();

        Assert.Equal([JobStatus.InProgress, JobStatus.OnHold], targets);
        Assert.Equal(targets, allowed);
    }

    [Fact]
    public async Task AfterTheDemoSeedMarcoReyesStartsHoldsAndResumesJob1047AndDanaReadsTheHistory()
    {
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
            var marco = await db.Users.AsNoTracking().SingleAsync(user => user.Email == "marco.reyes@fieldservice.example", CancellationToken);
            var seeded = await db.Jobs.AsNoTracking().SingleAsync(job => job.Number == 1047, CancellationToken);
            Assert.Equal(JobStatus.Assigned, seeded.Status);
            Assert.Equal(marco.Id, seeded.AssignedTechnicianId);
            using var technicianClient = factory.CreateApiClient();
            technicianClient.AccessToken = TestTokens.For(factory, marco);
            using var administratorClient = factory.CreateApiClient();
            administratorClient.AccessToken = TestTokens.For(factory, dana);

            using var start = await technicianClient.PutJsonAsync(PathOf(seeded.Id), Body(JobStatus.InProgress, null, seeded.Version));
            var started = await ReadJobAsync(start);
            factory.Clock.Advance(TimeSpan.FromMinutes(2));
            using var hold = await technicianClient.PutJsonAsync(
                PathOf(seeded.Id),
                Body(JobStatus.OnHold, "Waiting for a replacement thermostat", started.Version));
            var onHold = await ReadJobAsync(hold);
            factory.Clock.Advance(TimeSpan.FromMinutes(2));
            using var resume = await technicianClient.PutJsonAsync(PathOf(seeded.Id), Body(JobStatus.InProgress, null, onHold.Version));
            var resumed = await ReadJobAsync(resume);
            using var stale = await technicianClient.PutJsonAsync(
                PathOf(seeded.Id),
                Body(JobStatus.OnHold, "A second device", onHold.Version));
            using var read = await administratorClient.GetAsync($"/api/jobs/{seeded.Id}");

            Assert.Equal([new AllowedStatusChange(JobStatus.OnHold, RequiresNote: true)], started.Affordances.AllowedStatusChanges);
            Assert.Equal([new AllowedStatusChange(JobStatus.InProgress, RequiresNote: false)], onHold.Affordances.AllowedStatusChanges);
            Assert.Equal(JobStatus.InProgress, resumed.Status);
            await ApiClient.AssertProblemAsync(stale, HttpStatusCode.Conflict, "concurrency.conflict");
            var details = await ReadJobAsync(read);
            Assert.Equal(
                [JobHistoryAction.Created, JobHistoryAction.Assigned, JobHistoryAction.Started, JobHistoryAction.PutOnHold, JobHistoryAction.Resumed],
                details.History.Select(entry => entry.Action));
            Assert.Equal("Waiting for a replacement thermostat", details.History[3].Note);
            Assert.All(details.History.Skip(2), entry => Assert.Equal(new UserReference(marco.Id, "Marco Reyes"), entry.Actor));
            Assert.Empty(details.Affordances.AllowedStatusChanges);
            Assert.Equal(
                [JobStatus.Assigned, JobStatus.InProgress, JobStatus.OnHold],
                factory.Events.Events
                    .Where(jobEvent => jobEvent.Type == JobEventTypes.StatusChanged && jobEvent.JobId == seeded.Id)
                    .Select(jobEvent => jobEvent.PreviousStatus!.Value));
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    public static object Body(JobStatus status, string? note, uint version) => new { status, note, version };

    public static string PathOf(Guid jobId) => $"/api/jobs/{jobId}/status";

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

    private async Task<(Job Created, Technician Technician, Job Job)> ArrangeAsync(JobStatus status)
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var technician = await DataAt(Earlier).CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var created = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        var job = await DataAt(Earlier.AddMinutes(5)).MoveToAsync(created, technician, status, CancellationToken);
        return (created, technician, job);
    }

    private TestData DataAt(DateTimeOffset now) => new(Fixture, now);

    private ApiClient ClientFor(User user)
    {
        var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TestTokens.For(Fixture.Factory, user);
        return client;
    }

    private async Task<int> HistoryCountAsync(Guid jobId)
    {
        await using var db = Fixture.CreateAppDbContext();
        return await db.JobHistory.CountAsync(entry => entry.JobId == jobId, CancellationToken);
    }

    private Task AssertUnchangedAsync(Job job) => AssertUnchangedAsync(job, Fixture.Factory.Events);

    private async Task AssertUnchangedAsync(Job job, RecordingJobEventQueue events)
    {
        var requestInstant = Now;
        await using var db = Fixture.CreateAppDbContext();
        var stored = await db.Jobs.AsNoTracking().SingleAsync(entry => entry.Id == job.Id, CancellationToken);
        Assert.Equal(job.Version, stored.Version);
        Assert.Equal(job.Status, stored.Status);
        Assert.Equal(job.StartedAt, stored.StartedAt);
        Assert.Equal(job.UpdatedAt, stored.UpdatedAt);
        Assert.True(stored.UpdatedAt < requestInstant);
        Assert.Equal(
            0,
            await db.JobHistory.CountAsync(
                entry => entry.JobId == job.Id && entry.OccurredAt >= requestInstant,
                CancellationToken));
        events.AssertEmpty();
    }

    private sealed class CommitRefusal : DbTransactionInterceptor
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _attempts);
            throw new InvalidOperationException("The commit is refused so the save rolls back.");
        }
    }
}
