using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Features;
using FieldService.Api.Features.Inspections;
using FieldService.Api.Features.Jobs;
using FieldService.Api.Features.Jobs.CancelJob;
using FieldService.Api.Features.Technicians.GetAssignedJobs;
using FieldService.Api.Persistence;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Shared.Errors;
using FieldService.Api.Shared.Realtime;
using FieldService.Api.Tests.Features.Inspections;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FieldService.Api.Tests.Features.Jobs;

[Collection(DatabaseCollection.Name)]
public sealed class CancelJobTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string LengthMessage = "Enter at most 500 characters.";

    private const string VersionMessage = "Send the job version you last read.";

    private const string InvalidValueMessage = "The value is not valid.";

    private const string Reason = "The customer postponed the visit until the new parts arrive.";

    private static readonly Guid _templateId = ChecklistTemplateSeed.TemplateId(2);

    private DateTimeOffset Now => UtcInstant.Truncate(Fixture.Factory.Clock.GetUtcNow());

    public static TheoryData<string, string, string> InvalidBodies => new()
    {
        { """{"version":1}""", "reason", CancelJobRequest.ReasonMessage },
        { """{"reason":"","version":1}""", "reason", CancelJobRequest.ReasonMessage },
        { """{"reason":"   ","version":1}""", "reason", CancelJobRequest.ReasonMessage },
        { """{"reason":"\n\t","version":1}""", "reason", CancelJobRequest.ReasonMessage },
        { """{"reason":null,"version":1}""", "reason", InvalidValueMessage },
        { """{"reason":42,"version":1}""", "reason", InvalidValueMessage },
        { $$"""{"reason":"{{new string('r', 501)}}","version":1}""", "reason", LengthMessage },
        { """{"reason":"Stop\u0000now","version":1}""", "reason", MultiLineAttribute.Message },
        { """{"reason":"Stop\u001bnow","version":1}""", "reason", MultiLineAttribute.Message },
        { """{"reason":"Stop\u0085now","version":1}""", "reason", MultiLineAttribute.Message },
        { """{"reason":"The customer cancelled."}""", "version", VersionMessage },
        { """{"reason":"The customer cancelled.","version":null}""", "version", VersionMessage },
        { """{"reason":"The customer cancelled.","version":"41877"}""", "version", InvalidValueMessage },
        { """{"reason":"The customer cancelled.","version":-1}""", "version", InvalidValueMessage },
        { """{"reason":"The customer cancelled.","version":4294967296}""", "version", InvalidValueMessage },
        { """{"reason":"The customer cancelled.","version":1.5}""", "version", InvalidValueMessage },
        { """{"reason":"The customer cancelled.","reason":"Again.","version":1}""", "reason", InvalidValueMessage },
        { "[]", "body", "The request body is missing or is not valid JSON." },
    };

    [Theory]
    [InlineData(JobStatus.Unassigned)]
    [InlineData(JobStatus.Assigned)]
    [InlineData(JobStatus.InProgress)]
    [InlineData(JobStatus.OnHold)]
    public async Task CancellingAnOpenJobSetsCancelledAtAddsACancelledEntryWithTheTrimmedReasonAndKeepsTheAssignee(JobStatus status)
    {
        var (administrator, job) = await ArrangeAsync(status);
        var historyCount = await HistoryCountAsync(job.Id);
        using var client = ClientFor(administrator);

        using var response = await client.PostJsonAsync(PathOf(job.Id), Body($"  {Reason}\n ", job.Version));

        var details = await ReadJobAsync(response);
        Assert.Equal(JobStatus.Cancelled, details.Status);
        Assert.Equal(Now, details.CancelledAt);
        Assert.Equal(Now, details.UpdatedAt);
        Assert.Null(details.CompletedAt);
        Assert.Equal(job.AssignedTechnicianId, details.AssignedTechnician?.Id);
        Assert.Equal(job.AssignedAt, details.AssignedAt);
        Assert.Equal(job.StartedAt, details.StartedAt);
        Assert.Equal(job.CreatedAt, details.CreatedAt);
        Assert.NotEqual(job.Version, details.Version);
        Assert.Equal(historyCount + 1, details.History.Count);
        var cancelled = details.History[^1];
        Assert.Equal(JobHistoryAction.Cancelled, cancelled.Action);
        Assert.Equal(status, cancelled.FromStatus);
        Assert.Equal(JobStatus.Cancelled, cancelled.ToStatus);
        Assert.Equal(new UserReference(administrator.Id, administrator.DisplayName), cancelled.Actor);
        Assert.Null(cancelled.Technician);
        Assert.Equal(Reason, cancelled.Note);
        Assert.Equal(Now, cancelled.OccurredAt);
        Assert.Empty(details.Affordances.AllowedStatusChanges);
        Assert.Equal(new SubmitAffordance(false, "auth.forbidden", 0, 10), details.Affordances.CanSubmit);
        Assert.False(details.Affordances.CanAssign);
        Assert.False(details.Affordances.CanCancel);

        await using var db = Fixture.CreateAppDbContext();
        var stored = await db.Jobs.AsNoTracking().SingleAsync(entry => entry.Id == job.Id, CancellationToken);
        Assert.Equal(JobStatus.Cancelled, stored.Status);
        Assert.Equal(Now, stored.CancelledAt);
        Assert.Equal(stored.CancelledAt, stored.UpdatedAt);
        Assert.Null(stored.CompletedAt);
        Assert.Equal(details.Version, stored.Version);
        Assert.Equal(job.AssignedTechnicianId, stored.AssignedTechnicianId);
        Assert.Equal(job.AssignedAt, stored.AssignedAt);
        Assert.Equal(job.StartedAt, stored.StartedAt);
        var entries = await db.JobHistory.AsNoTracking().Where(entry => entry.JobId == job.Id).ToListAsync(CancellationToken);
        Assert.Equal(historyCount + 1, entries.Count);
        var storedEntry = Assert.Single(entries, entry => entry.Action == JobHistoryAction.Cancelled);
        Assert.Equal((status, JobStatus.Cancelled, administrator.Id, (Guid?)null, Reason, Now), (storedEntry.FromStatus!.Value, storedEntry.ToStatus, storedEntry.ActorUserId, storedEntry.TechnicianId, storedEntry.Note, storedEntry.OccurredAt));
    }

    [Theory]
    [InlineData(JobStatus.Unassigned)]
    [InlineData(JobStatus.Assigned)]
    [InlineData(JobStatus.InProgress)]
    [InlineData(JobStatus.OnHold)]
    public async Task OneJobStatusChangedEventWithCancelledAndThePreviousStatusIsRecordedOnlyAfterTheCommit(JobStatus status)
    {
        var (administrator, job) = await ArrangeAsync(status);
        using var client = ClientFor(administrator);
        bool? committedWhenEnqueued = null;
        Fixture.Factory.Events.OnEnqueue = jobEvent =>
        {
            using var db = Fixture.CreateAppDbContext();
            committedWhenEnqueued = db.Jobs.AsNoTracking().Any(entry => entry.Id == jobEvent.JobId && entry.Status == JobStatus.Cancelled)
                && db.JobHistory.AsNoTracking().Any(entry => entry.JobId == jobEvent.JobId && entry.Action == JobHistoryAction.Cancelled);
        };

        using var response = await client.PostJsonAsync(PathOf(job.Id), Body(Reason, job.Version));

        var details = await ReadJobAsync(response);
        var recorded = Assert.Single(Fixture.Factory.Events.Events);
        Assert.True(committedWhenEnqueued);
        Assert.Equal(JobEventTypes.StatusChanged, recorded.Type);
        Assert.Equal(JobEvent.CurrentVersion, recorded.Version);
        Assert.Equal(job.Id, recorded.JobId);
        Assert.Equal(job.Number, recorded.JobNumber);
        Assert.Equal(JobStatus.Cancelled, recorded.Status);
        Assert.Equal(status, recorded.PreviousStatus);
        Assert.Equal(details.CancelledAt, recorded.OccurredAt);
        Assert.Equal(client.LastCorrelationId, recorded.CorrelationId);
    }

    [Theory]
    [InlineData("longest")]
    [InlineData("lines")]
    [InlineData("outside the basic plane")]
    public async Task TheReasonIsStoredTrimmedWithItsLineBreaksAndTabsUpToFiveHundredCharacters(string kind)
    {
        var (administrator, job) = await ArrangeAsync(JobStatus.Assigned);
        using var client = ClientFor(administrator);
        (string Sent, string Stored) reason = kind switch
        {
            "longest" => (new string('r', 500), new string('r', 500)),
            "lines" => ("\tParts back-ordered.\r\nRebook\tnext week.\n", "Parts back-ordered.\r\nRebook\tnext week."),
            _ => (string.Concat(Enumerable.Repeat("\U0001F527", 250)), string.Concat(Enumerable.Repeat("\U0001F527", 250))),
        };

        using var response = await client.PostJsonAsync(PathOf(job.Id), Body(reason.Sent, job.Version));

        Assert.Equal(reason.Stored, (await ReadJobAsync(response)).History[^1].Note);
        await using var db = Fixture.CreateAppDbContext();
        Assert.Equal(
            reason.Stored,
            (await db.JobHistory.AsNoTracking().SingleAsync(entry => entry.JobId == job.Id && entry.Action == JobHistoryAction.Cancelled, CancellationToken)).Note);
    }

    [Fact]
    public async Task ACompletedJobReturns409InvalidTransitionWhateverTheVersionAndNothingIsWritten()
    {
        var (administrator, job) = await ArrangeAsync(JobStatus.Completed);
        var state = await StateOfAsync(job.Id);
        using var client = ClientFor(administrator);

        using var current = await client.PostJsonAsync(PathOf(job.Id), Body(Reason, job.Version));
        using var stale = await client.PostJsonAsync(PathOf(job.Id), Body(Reason, job.Version + 7));

        foreach (var response in new[] { current, stale })
        {
            using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Conflict, "job.invalid-transition");
            Assert.Equal("The job cannot move to that status.", problem.RootElement.GetProperty("title").GetString());
            Assert.Equal(
                $"{JobNumber.Format(job.Number)} cannot move from Completed to Cancelled.",
                problem.RootElement.GetProperty("detail").GetString());
        }

        Assert.Equal(state, await StateOfAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task CancellingAJobThatIsAlreadyCancelledReturnsItUnchangedWithNoEventWhateverTheVersionOrReason()
    {
        var (administrator, job) = await ArrangeAsync(JobStatus.Cancelled);
        var state = await StateOfAsync(job.Id);
        using var client = ClientFor(administrator);

        using var current = await client.PostJsonAsync(PathOf(job.Id), Body("A different reason entirely.", job.Version));
        using var stale = await client.PostJsonAsync(PathOf(job.Id), Body(Reason, job.Version + 9));

        foreach (var response in new[] { current, stale })
        {
            var details = await ReadJobAsync(response);
            Assert.Equal(JobStatus.Cancelled, details.Status);
            Assert.Equal(job.Version, details.Version);
            Assert.Equal(job.UpdatedAt, details.UpdatedAt);
            Assert.Equal(job.CancelledAt, details.CancelledAt);
            Assert.Equal(TestData.CancellationReason, Assert.Single(details.History, entry => entry.Action == JobHistoryAction.Cancelled).Note);
            Assert.False(details.Affordances.CanCancel);
        }

        Assert.Equal(state, await StateOfAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task AStaleVersionReturns409ConcurrencyConflictAndWritesNothing()
    {
        var (administrator, technician, job) = await ArrangeWithTechnicianAsync(JobStatus.Assigned);
        await DataAt(Now.AddMinutes(-30)).ChangeStatusAsync(job, JobStatus.InProgress, CancellationToken);
        var state = await StateOfAsync(job.Id);
        using var client = ClientFor(administrator);

        using var response = await client.PostJsonAsync(PathOf(job.Id), Body(Reason, job.Version));

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.Conflict, "concurrency.conflict");
        Assert.Equal(state, await StateOfAsync(job.Id));
        Assert.Equal(technician.UserId, (await LoadJobAsync(job.Id)).AssignedTechnicianId);
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task AnUnknownJobReturns404JobNotFoundWithoutADetail()
    {
        var administrator = await DataAt(Now).CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.PostJsonAsync(PathOf(Guid.CreateVersion7()), Body(Reason, 1));

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.NotFound, "job.not-found");
        Assert.False(problem.RootElement.TryGetProperty("detail", out _));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task AnInvalidBodyIsAFieldErrorKeyedByTheMemberAndNothingIsWritten(string body, string key, string message)
    {
        var (administrator, job) = await ArrangeAsync(JobStatus.Assigned);
        var state = await StateOfAsync(job.Id);
        using var client = ClientFor(administrator);

        using var response = await client.PostAsync(PathOf(job.Id), Json(body));

        await ApiClient.AssertFieldErrorsAsync(response, key);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(message, Assert.Single(problem.RootElement.GetProperty("errors").GetProperty(key).EnumerateArray()).GetString());
        Assert.Equal(state, await StateOfAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task AnEmptyObjectReportsTheReasonAndTheVersionTogether()
    {
        var (administrator, job) = await ArrangeAsync(JobStatus.Unassigned);
        using var client = ClientFor(administrator);

        using var response = await client.PostAsync(PathOf(job.Id), Json("{}"));

        await ApiClient.AssertFieldErrorsAsync(response, "reason", "version");
    }

    [Fact]
    public async Task RequestValidationComesBeforeTheJobLookupSoAnUnknownJobWithAnInvalidBodyGets400()
    {
        var administrator = await new TestData(Fixture, Now).CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.PostAsync(PathOf(Guid.CreateVersion7()), Json("""{"reason":" ","version":1}"""));

        await ApiClient.AssertFieldErrorsAsync(response, "reason");
    }

    [Fact]
    public async Task AnInvalidReasonIsA400EvenForAJobThatIsAlreadyCancelled()
    {
        var (administrator, job) = await ArrangeAsync(JobStatus.Cancelled);
        var state = await StateOfAsync(job.Id);
        using var client = ClientFor(administrator);

        using var response = await client.PostJsonAsync(PathOf(job.Id), Body("   ", job.Version));

        await ApiClient.AssertFieldErrorsAsync(response, "reason");
        Assert.Equal(state, await StateOfAsync(job.Id));
    }

    [Fact]
    public async Task TheReasonMessageNeverEchoesTheValueSent()
    {
        var (administrator, job) = await ArrangeAsync(JobStatus.Assigned);
        using var client = ClientFor(administrator);
        var marker = $"marker{Guid.NewGuid():N}";

        using var response = await client.PostAsync(
            PathOf(job.Id),
            Json($$"""{"reason":"{{marker}}{{new string('r', 500)}}","version":1}"""));

        var text = await response.Content.ReadAsStringAsync(CancellationToken);
        await ApiClient.AssertFieldErrorsAsync(response, "reason");
        Assert.DoesNotContain(marker, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAssignedTechnicianAndAnotherTechnicianGet403EvenWithAnInvalidBodyAndNothingIsWritten()
    {
        var data = new TestData(Fixture, Now);
        var (_, job) = await ArrangeAsync(JobStatus.Unassigned);
        var technician = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var other = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        job = await data.AssignJobAsync(job, technician, CancellationToken);
        var state = await StateOfAsync(job.Id);
        using var assignee = ClientFor(technician.User);
        using var stranger = ClientFor(other.User);

        using var valid = await assignee.PostJsonAsync(PathOf(job.Id), new { reason = TestData.CancellationReason, version = job.Version });
        using var invalid = await assignee.PostJsonAsync(PathOf(job.Id), new { });
        using var fromStranger = await stranger.PostJsonAsync(PathOf(job.Id), new { reason = TestData.CancellationReason, version = job.Version });

        await ApiClient.AssertProblemAsync(valid, HttpStatusCode.Forbidden, "auth.forbidden");
        await ApiClient.AssertProblemAsync(invalid, HttpStatusCode.Forbidden, "auth.forbidden");
        await ApiClient.AssertProblemAsync(fromStranger, HttpStatusCode.Forbidden, "auth.forbidden");
        Assert.Equal(state, await StateOfAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task AnAnonymousCallerGets401AndNothingIsWritten()
    {
        var (_, job) = await ArrangeAsync(JobStatus.Assigned);
        var state = await StateOfAsync(job.Id);
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.PostJsonAsync(PathOf(job.Id), new { reason = TestData.CancellationReason, version = job.Version });

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        Assert.Equal(state, await StateOfAsync(job.Id));
    }

    [Fact]
    public async Task AnExpiredTokenAndATokenSignedWithAnotherKeyGet401AndNothingIsWritten()
    {
        var (administrator, job) = await ArrangeAsync(JobStatus.Assigned);
        var state = await StateOfAsync(job.Id);
        await using var expiring = Fixture.CreateFactory();
        await using var foreign = Fixture.CreateFactory();
        using var expired = expiring.CreateApiClient();
        expired.AccessToken = TestTokens.For(expiring, administrator);
        expiring.Clock.Advance(TimeSpan.FromMinutes(16));
        using var signedElsewhere = Fixture.Factory.CreateApiClient();
        signedElsewhere.AccessToken = TestTokens.For(foreign, administrator);

        using var withExpired = await expired.PostJsonAsync(PathOf(job.Id), Body(Reason, job.Version));
        using var withForeign = await signedElsewhere.PostJsonAsync(PathOf(job.Id), Body(Reason, job.Version));

        await ApiClient.AssertProblemAsync(withExpired, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        await ApiClient.AssertProblemAsync(withForeign, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        Assert.Equal(state, await StateOfAsync(job.Id));
        expiring.Events.AssertEmpty();
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task AJobIdThatIsNotAGuidMatchesNoRouteAMissingBodyGets400AnotherMediaTypeGets415AndGetIsNotAllowed()
    {
        var (administrator, job) = await ArrangeAsync(JobStatus.Assigned);
        using var client = ClientFor(administrator);

        using var notAGuid = await client.PostJsonAsync("/api/jobs/1042/cancellation", new { reason = TestData.CancellationReason, version = job.Version });
        using var missing = await client.PostAsync(PathOf(job.Id));
        using var text = await client.PostAsync(
            PathOf(job.Id),
            new StringContent("""{"reason":"Not needed.","version":1}""", Encoding.UTF8, "text/plain"));
        using var get = await client.GetAsync(PathOf(job.Id));

        await ApiClient.AssertProblemAsync(notAGuid, HttpStatusCode.NotFound, "resource.not-found");
        await ApiClient.AssertFieldErrorsAsync(missing, "body");
        await ApiClient.AssertProblemAsync(text, HttpStatusCode.UnsupportedMediaType, "request.unsupported-media-type");
        await ApiClient.AssertProblemAsync(get, HttpStatusCode.MethodNotAllowed, "request.method-not-allowed");
    }

    [Theory]
    [InlineData(JobStatus.InProgress)]
    [InlineData(JobStatus.OnHold)]
    public async Task ADraftInspectionStaysUnsubmittedAndUnchangedAndAfterwardsRefusesEveryChange(JobStatus status)
    {
        var (administrator, technician, job) = await ArrangeWithTechnicianAsync(status, answered: 6);
        var draftBefore = await DraftOfAsync(job.Id);
        using var dispatcher = ClientFor(administrator);
        using var device = ClientFor(technician.User);

        using var cancelled = await dispatcher.PostJsonAsync(PathOf(job.Id), Body(Reason, job.Version));
        var details = await ReadJobAsync(cancelled);
        var state = await StateOfAsync(job.Id);

        Assert.Equal(draftBefore, await DraftOfAsync(job.Id));
        Assert.Contains("submitted=|", draftBefore, StringComparison.Ordinal);
        using var save = await device.PutJsonAsync(SaveInspectionTests.PathOf(job.Id), SaveInspectionTests.Body(details.Version, SaveInspectionTests.Answer(7, "Pass")));
        using var submit = await device.PostJsonAsync(SubmitJobTests.PathOf(job.Id), SubmitJobTests.Body(null, true, details.Version));
        using var resume = await device.PutJsonAsync(UpdateJobStatusTests.PathOf(job.Id), UpdateJobStatusTests.Body(JobStatus.InProgress, null, details.Version));
        using var hold = await device.PutJsonAsync(UpdateJobStatusTests.PathOf(job.Id), UpdateJobStatusTests.Body(JobStatus.OnHold, TestData.HoldNote, details.Version));
        using var assign = await dispatcher.PutJsonAsync(AssignJobTests.PathOf(job.Id), AssignJobTests.Body(technician.UserId, details.Version));
        using var read = await device.GetAsync(SaveInspectionTests.PathOf(job.Id));

        using (var problem = await ApiClient.AssertProblemAsync(save, HttpStatusCode.Conflict, "inspection.locked"))
        {
            Assert.Equal(
                $"{JobNumber.Format(job.Number)} is Cancelled, so its inspection cannot be changed.",
                problem.RootElement.GetProperty("detail").GetString());
        }

        await ApiClient.AssertProblemAsync(submit, HttpStatusCode.Conflict, "job.invalid-transition");
        await ApiClient.AssertProblemAsync(resume, HttpStatusCode.Conflict, "job.invalid-transition");
        await ApiClient.AssertProblemAsync(hold, HttpStatusCode.Conflict, "job.invalid-transition");
        await ApiClient.AssertProblemAsync(assign, HttpStatusCode.Conflict, "job.not-assignable");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var inspection = (await read.Content.ReadFromJsonAsync<InspectionResponse>(ApiClient.Json, CancellationToken))!;
        Assert.Equal(JobStatus.Cancelled, inspection.JobStatus);
        Assert.False(inspection.Affordances.CanEdit);
        Assert.Equal(new SubmitAffordance(false, "job.invalid-transition", 6, 10), inspection.Affordances.CanSubmit);
        Assert.Null(inspection.SubmittedAt);
        Assert.Equal(6, inspection.Progress.Answered);
        Assert.Equal(state, await StateOfAsync(job.Id));
        Assert.Equal(draftBefore, await DraftOfAsync(job.Id));
        Assert.Single(Fixture.Factory.Events.Events);
    }

    [Fact]
    public async Task TheAssignedTechnicianSeesTheCancelledJobReadOnlyWithItsReasonInTheCompletedView()
    {
        var (administrator, technician, job) = await ArrangeWithTechnicianAsync(JobStatus.Assigned);
        using var dispatcher = ClientFor(administrator);
        using var device = ClientFor(technician.User);
        using (var cancelled = await dispatcher.PostJsonAsync(PathOf(job.Id), Body(Reason, job.Version)))
        {
            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        }

        using var read = await device.GetAsync($"/api/jobs/{job.Id}");
        using var completedView = await device.GetAsync("/api/technicians/me/jobs?view=completed");
        using var activeView = await device.GetAsync("/api/technicians/me/jobs?view=active");

        var details = await ReadJobAsync(read);
        Assert.Equal(JobStatus.Cancelled, details.Status);
        Assert.Equal(technician.UserId, details.AssignedTechnician?.Id);
        Assert.Empty(details.Affordances.AllowedStatusChanges);
        Assert.Equal(new SubmitAffordance(false, "job.invalid-transition", 0, 10), details.Affordances.CanSubmit);
        Assert.False(details.Affordances.CanAssign);
        Assert.False(details.Affordances.CanCancel);
        Assert.Equal((JobHistoryAction.Cancelled, Reason), (details.History[^1].Action, details.History[^1].Note));
        var completed = (await completedView.Content.ReadFromJsonAsync<GetAssignedJobsResponse>(ApiClient.Json, CancellationToken))!;
        var item = Assert.Single(completed.Items);
        Assert.Equal((job.Id, JobStatus.Cancelled, details.CancelledAt), (item.Id, item.Status, item.CancelledAt));
        Assert.Empty((await activeView.Content.ReadFromJsonAsync<GetAssignedJobsResponse>(ApiClient.Json, CancellationToken))!.Items);
    }

    [Fact]
    public async Task ARolledBackCancellationReturns500WritesNothingAndRecordsNoEvent()
    {
        var refusal = new CommitRefusal();
        await using var factory = Fixture.CreateFactory(factory => factory.TestServices = services =>
            services.ConfigureDbContext<FieldServiceDbContext>(options => options.AddInterceptors(refusal)));
        var (administrator, job) = await ArrangeAsync(JobStatus.Assigned);
        var state = await StateOfAsync(job.Id);
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, administrator);

        using var response = await client.PostJsonAsync(PathOf(job.Id), Body(Reason, job.Version));

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.InternalServerError, "server.error");
        Assert.Equal(1, refusal.Attempts);
        Assert.Equal(state, await StateOfAsync(job.Id));
        factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task ACancellationIsOneBatchThatInsertsTheHistoryEntryThenUpdatesTheJobByVersionAndNeverTouchesTheInspection()
    {
        await using var factory = Fixture.CreateFactory(factory =>
        {
            factory.Settings["Serilog:MinimumLevel:Default"] = "Information";
            factory.Settings["Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore"] = "Information";
        });
        var (administrator, _, job) = await ArrangeWithTechnicianAsync(JobStatus.InProgress, answered: 6);
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, administrator);
        factory.LogSink.Clear();

        using var response = await client.PostJsonAsync(PathOf(job.Id), Body(Reason, job.Version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var commands = CommandsOf(factory);
        Assert.Equal(5, commands.Count);
        Assert.StartsWith("SELECT", commands[0], StringComparison.Ordinal);
        Assert.Contains("FROM fieldservice.jobs", commands[0], StringComparison.Ordinal);
        var write = commands[1];
        Assert.StartsWith("INSERT INTO fieldservice.job_history ", write, StringComparison.Ordinal);
        var update = write.IndexOf("UPDATE fieldservice.jobs SET cancelled_at = @", StringComparison.Ordinal);
        Assert.True(update > 0, write);
        Assert.Contains("status = @", write[update..], StringComparison.Ordinal);
        Assert.Contains("updated_at = @", write[update..], StringComparison.Ordinal);
        Assert.Contains("WHERE id = @", write[update..], StringComparison.Ordinal);
        Assert.Contains("AND xmin = @", write[update..], StringComparison.Ordinal);
        Assert.DoesNotContain("inspection", write, StringComparison.Ordinal);
        Assert.All(commands.Skip(2), command => Assert.StartsWith("SELECT", command, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancellingLogsNoReasonNameEmailOrJobTextAtInformationLevel()
    {
        var (administrator, technician, job) = await ArrangeWithTechnicianAsync(JobStatus.Assigned);
        const string Sentinel = "Harbor Supply recalled the condenser for the Elm Street site";
        await using var factory = Fixture.CreateFactory(factory =>
        {
            factory.Settings["Serilog:MinimumLevel:Default"] = "Information";
            factory.Settings["Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore"] = "Information";
        });
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, administrator);

        using var response = await client.PostJsonAsync(PathOf(job.Id), Body(Sentinel, job.Version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(factory.LogSink.Events);
        foreach (var text in new[]
        {
            Sentinel, "Harbor Supply", administrator.DisplayName, administrator.Email, technician.User.DisplayName,
            technician.User.Email, technician.PhoneNumber, job.Title, job.CustomerName, job.SiteAddress,
        })
        {
            Assert.All(factory.LogSink.Events, logEvent =>
                Assert.DoesNotContain(text, CapturingLogSink.Text(logEvent), StringComparison.OrdinalIgnoreCase));
        }
    }

    public static string PathOf(Guid jobId) => $"/api/jobs/{jobId}/cancellation";

    public static object Body(string reason, uint version) => new { reason, version };

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static List<string> CommandsOf(ApiFactory factory)
    {
        var commands = factory.LogSink.Events
            .Where(entry => CapturingLogSink.Scalar(entry, "SourceContext") == "Microsoft.EntityFrameworkCore.Database.Command"
                && CapturingLogSink.Scalar(entry, "commandText") is not null)
            .Select(entry => CapturingLogSink.Scalar(entry, "commandText")!)
            .ToList();
        factory.LogSink.Clear();
        return commands;
    }

    private static async Task<JobDetailsResponse> ReadJobAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var job = await response.Content.ReadFromJsonAsync<JobDetailsResponse>(ApiClient.Json, TestContext.Current.CancellationToken);
        return job ?? throw new InvalidOperationException("The response has no job body.");
    }

    private async Task<(User Administrator, Job Job)> ArrangeAsync(JobStatus status)
    {
        var (administrator, _, job) = await ArrangeWithTechnicianAsync(status);
        return (administrator, job);
    }

    private async Task<(User Administrator, Technician Technician, Job Job)> ArrangeWithTechnicianAsync(JobStatus status, int answered = 0)
    {
        var earlier = Now.AddHours(-2);
        var administrator = await DataAt(earlier).CreateAdministratorAsync(CancellationToken);
        var technician = await DataAt(earlier).CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var created = await DataAt(earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        if (answered == 0)
        {
            await DataAt(earlier.AddMinutes(5)).MoveToAsync(created, technician, status, CancellationToken);
            return (administrator, technician, await LoadJobAsync(created.Id));
        }

        var job = await DataAt(earlier.AddMinutes(5)).MoveToAsync(created, technician, JobStatus.InProgress, CancellationToken);
        await DataAt(earlier.AddMinutes(10)).SaveInspectionAsync(
            job,
            TestData.PassingAnswers(TestData.ChecklistOf(_templateId), answered),
            CancellationToken);
        if (status == JobStatus.OnHold)
        {
            await DataAt(earlier.AddMinutes(15)).ChangeStatusAsync(job, JobStatus.OnHold, CancellationToken, TestData.HoldNote);
        }

        return (administrator, technician, await LoadJobAsync(job.Id));
    }

    private async Task<Job> LoadJobAsync(Guid jobId)
    {
        await using var db = Fixture.CreateAppDbContext();
        return await db.Jobs.AsNoTracking().SingleAsync(entry => entry.Id == jobId, CancellationToken);
    }

    private async Task<int> HistoryCountAsync(Guid jobId)
    {
        await using var db = Fixture.CreateAppDbContext();
        return await db.JobHistory.CountAsync(entry => entry.JobId == jobId, CancellationToken);
    }

    private async Task<string> StateOfAsync(Guid jobId)
    {
        await using var db = Fixture.CreateAppDbContext();
        var job = await db.Jobs.AsNoTracking().SingleAsync(entry => entry.Id == jobId, CancellationToken);
        var history = await db.JobHistory.CountAsync(entry => entry.JobId == jobId, CancellationToken);
        var draft = await DraftOfAsync(jobId);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{job.Version}|{job.Status}|{job.UpdatedAt:O}|{job.CancelledAt:O}|{job.CompletedAt:O}|{job.AssignedTechnicianId}|{history}|{draft}");
    }

    private async Task<string> DraftOfAsync(Guid jobId)
    {
        await using var db = Fixture.CreateAppDbContext();
        var inspection = await db.Inspections.AsNoTracking().WithAnswersAndReadings().SingleOrDefaultAsync(entry => entry.JobId == jobId, CancellationToken);
        if (inspection is null)
        {
            return "none";
        }

        var answers = string.Join(
            ',',
            inspection.Answers
                .OrderBy(answer => answer.ChecklistItemId)
                .Select(answer => string.Create(CultureInfo.InvariantCulture, $"{answer.ChecklistItemId}:{answer.Result}:{answer.AnsweredAt:O}:{answer.Readings.Count}")));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"updated={inspection.UpdatedAt:O}|submitted={inspection.SubmittedAt:O}|notes={inspection.TechnicianNotes}|{answers}");
    }

    private TestData DataAt(DateTimeOffset now) => new(Fixture, now);

    private ApiClient ClientFor(User user)
    {
        var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TestTokens.For(Fixture.Factory, user);
        return client;
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
            throw new InvalidOperationException("The commit is refused so the cancellation rolls back.");
        }
    }
}
