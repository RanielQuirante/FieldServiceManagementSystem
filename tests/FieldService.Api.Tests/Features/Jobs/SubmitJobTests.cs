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
using FieldService.Api.Features.Jobs.SubmitJob;
using FieldService.Api.Persistence;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Shared.Realtime;
using FieldService.Api.Tests.Features.Inspections;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FieldService.Api.Tests.Features.Jobs;

[Collection(DatabaseCollection.Name)]
public sealed class SubmitJobTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string Invalid = "job.invalid-transition";

    private const string Notes = "Recharged the system to spec and cleaned the condenser coil.";

    private static readonly Guid _templateId = ChecklistTemplateSeed.TemplateId(2);

    private DateTimeOffset Now => UtcInstant.Truncate(Fixture.Factory.Clock.GetUtcNow());

    private DateTimeOffset Earlier => Now.AddMinutes(-30);

    [Fact]
    public async Task SubmittingACompleteInspectionCompletesTheJobAndTheInspectionAtOneInstant()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress, answered: 10);
        var historyCount = await HistoryCountAsync(job.Id);
        using var client = ClientFor(technician.User);

        using var response = await client.PostJsonAsync(PathOf(job.Id), Body($"  {Notes}  ", true, job.Version));

        var details = await ReadJobAsync(response);
        Assert.Equal(JobStatus.Completed, details.Status);
        Assert.Equal(Now, details.CompletedAt);
        Assert.Equal(Now, details.UpdatedAt);
        Assert.Equal(job.StartedAt, details.StartedAt);
        Assert.NotEqual(job.Version, details.Version);
        var completed = details.History[^1];
        Assert.Equal(JobHistoryAction.Completed, completed.Action);
        Assert.Equal(JobStatus.InProgress, completed.FromStatus);
        Assert.Equal(JobStatus.Completed, completed.ToStatus);
        Assert.Equal(new UserReference(technician.UserId, technician.User.DisplayName), completed.Actor);
        Assert.Null(completed.Technician);
        Assert.Null(completed.Note);
        Assert.Equal(Now, completed.OccurredAt);
        Assert.Empty(details.Affordances.AllowedStatusChanges);
        Assert.Equal(new SubmitAffordance(false, Invalid, 10, 10), details.Affordances.CanSubmit);
        Assert.False(details.Affordances.CanAssign);

        await using var db = Fixture.CreateAppDbContext();
        var storedJob = await db.Jobs.AsNoTracking().SingleAsync(entry => entry.Id == job.Id, CancellationToken);
        Assert.Equal(JobStatus.Completed, storedJob.Status);
        Assert.Equal(Now, storedJob.CompletedAt);
        Assert.Equal(details.Version, storedJob.Version);
        var inspection = await db.Inspections.AsNoTracking().SingleAsync(entry => entry.JobId == job.Id, CancellationToken);
        Assert.Equal(Now, inspection.SubmittedAt);
        Assert.Equal(Now, inspection.UpdatedAt);
        Assert.Equal(Notes, inspection.TechnicianNotes);
        Assert.Equal(historyCount + 1, await HistoryCountAsync(job.Id));
        Assert.Equal(
            1,
            await db.JobHistory.CountAsync(entry => entry.JobId == job.Id && entry.Action == JobHistoryAction.Completed, CancellationToken));
    }

    [Fact]
    public async Task OneJobStatusChangedEventWithCompletedIsRecordedOnlyAfterTheCommit()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress, answered: 10);
        using var client = ClientFor(technician.User);
        bool? committedWhenEnqueued = null;
        Fixture.Factory.Events.OnEnqueue = jobEvent =>
        {
            using var db = Fixture.CreateAppDbContext();
            committedWhenEnqueued = db.Inspections.AsNoTracking()
                .Any(entry => entry.JobId == jobEvent.JobId && entry.SubmittedAt != null)
                && db.Jobs.AsNoTracking().Any(entry => entry.Id == jobEvent.JobId && entry.Status == JobStatus.Completed);
        };

        using var response = await client.PostJsonAsync(PathOf(job.Id), Body(null, true, job.Version));

        var details = await ReadJobAsync(response);
        var recorded = Assert.Single(Fixture.Factory.Events.Events);
        Assert.True(committedWhenEnqueued);
        Assert.Equal(JobEventTypes.StatusChanged, recorded.Type);
        Assert.Equal(job.Id, recorded.JobId);
        Assert.Equal(job.Number, recorded.JobNumber);
        Assert.Equal(JobStatus.Completed, recorded.Status);
        Assert.Equal(JobStatus.InProgress, recorded.PreviousStatus);
        Assert.Equal(details.CompletedAt, recorded.OccurredAt);
        Assert.Equal(client.LastCorrelationId, recorded.CorrelationId);
    }

    [Theory]
    [InlineData(0, "10 of 10 checklist items are unanswered.")]
    [InlineData(6, "4 of 10 checklist items are unanswered.")]
    [InlineData(9, "1 of 10 checklist items are unanswered.")]
    public async Task AnIncompleteOrMissingInspectionReturns409InspectionIncompleteWithTheCountAndWritesNothing(int answered, string detail)
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress, answered);
        var state = await StateOfAsync(job.Id);
        using var client = ClientFor(technician.User);

        using var response = await client.PostJsonAsync(PathOf(job.Id), Body(Notes, true, job.Version));

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Conflict, "inspection.incomplete");
        Assert.Equal("The inspection checklist is not complete.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.10", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(detail, problem.RootElement.GetProperty("detail").GetString());
        Assert.Equal(state, await StateOfAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Theory]
    [InlineData("""{"technicianNotes":null,"siteSafetyConfirmed":false,"version":@v}""", JobStatus.InProgress)]
    [InlineData("""{"technicianNotes":null,"version":@v}""", JobStatus.InProgress)]
    [InlineData("""{"technicianNotes":null,"siteSafetyConfirmed":null,"version":@v}""", JobStatus.InProgress)]
    [InlineData("""{"siteSafetyConfirmed":false,"version":@v}""", JobStatus.Completed)]
    [InlineData("""{"siteSafetyConfirmed":false,"version":@v}""", JobStatus.Assigned)]
    public async Task WithoutTheSiteSafetyConfirmationTheRequestIs400KeyedSiteSafetyConfirmedBeforeAnyStateRule(string template, JobStatus status)
    {
        var (technician, job) = await ArrangeAsync(status, answered: status == JobStatus.InProgress ? 10 : 0);
        var state = await StateOfAsync(job.Id);
        using var client = ClientFor(technician.User);

        using var response = await client.PostAsync(PathOf(job.Id), Json(SaveInspectionTests.Expand(template, job)));

        await ApiClient.AssertFieldErrorsAsync(response, "siteSafetyConfirmed");
        Assert.Equal([SubmitJobRequest.SiteSafetyMessage], await MessagesAsync(response, "siteSafetyConfirmed"));
        Assert.Equal(state, await StateOfAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Theory]
    [InlineData(JobStatus.Assigned, 0)]
    [InlineData(JobStatus.OnHold, 10)]
    [InlineData(JobStatus.Cancelled, 0)]
    public async Task AJobThatIsNotInProgressReturns409InvalidTransitionAndWritesNothing(JobStatus status, int answered)
    {
        var (technician, job) = await ArrangeAsync(status, answered);
        var state = await StateOfAsync(job.Id);
        using var client = ClientFor(technician.User);

        using var response = await client.PostJsonAsync(PathOf(job.Id), Body(Notes, true, job.Version));

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Conflict, Invalid);
        Assert.Equal(
            $"{JobNumber.Format(job.Number)} cannot move from {status} to Completed.",
            problem.RootElement.GetProperty("detail").GetString());
        Assert.Equal(state, await StateOfAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task SubmittingAJobThatIsAlreadyCompletedReturnsItUnchangedWithNoEventWhateverTheVersionOrNotes()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.Completed);
        var state = await StateOfAsync(job.Id);
        using var client = ClientFor(technician.User);

        using var current = await client.PostJsonAsync(PathOf(job.Id), Body(Notes, true, job.Version));
        using var stale = await client.PostJsonAsync(PathOf(job.Id), Body("Other notes", true, job.Version + 9));

        foreach (var response in new[] { current, stale })
        {
            var details = await ReadJobAsync(response);
            Assert.Equal(JobStatus.Completed, details.Status);
            Assert.Equal(job.Version, details.Version);
            Assert.Equal(job.UpdatedAt, details.UpdatedAt);
        }

        Assert.Equal(state, await StateOfAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task ASubmissionThatMeetsAnotherSubmissionCommittedAfterItsReadGets409InspectionLockedWithOneCompletedEntry()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress, answered: 10);
        var otherDevice = new BeforeFirstSaveChanges(() => DataAt(Earlier.AddMinutes(20)).SubmitJobAsync(job, CancellationToken, "First device"));
        await using var factory = Fixture.CreateFactory(factory => factory.TestServices = services =>
            services.ConfigureDbContext<FieldServiceDbContext>(options => options.AddInterceptors(otherDevice)));
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, technician.User);

        using var response = await client.PostJsonAsync(PathOf(job.Id), Body("Second device", true, job.Version));

        Assert.True(otherDevice.Ran);
        await ApiClient.AssertProblemAsync(response, HttpStatusCode.Conflict, "inspection.locked");
        await using var db = Fixture.CreateAppDbContext();
        var inspection = await db.Inspections.AsNoTracking().SingleAsync(entry => entry.JobId == job.Id, CancellationToken);
        Assert.Equal("First device", inspection.TechnicianNotes);
        Assert.Equal(
            1,
            await db.JobHistory.CountAsync(entry => entry.JobId == job.Id && entry.Action == JobHistoryAction.Completed, CancellationToken));
        Assert.DoesNotContain(factory.LogSink.Events, entry => entry.Level >= Serilog.Events.LogEventLevel.Error);
        factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task AStaleVersionReturns409ConcurrencyConflictAndWritesNothing()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress);
        await DataAt(Earlier.AddMinutes(10)).SaveInspectionAsync(
            job,
            TestData.PassingAnswers(TestData.ChecklistOf(_templateId)),
            CancellationToken);
        var state = await StateOfAsync(job.Id);
        using var client = ClientFor(technician.User);

        using var response = await client.PostJsonAsync(PathOf(job.Id), Body(Notes, true, job.Version));

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.Conflict, "concurrency.conflict");
        Assert.Equal(state, await StateOfAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task ABrokenRuleWinsOverAStaleVersion()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress, answered: 6);
        var (_, onHold) = await ArrangeAsync(JobStatus.OnHold, answered: 10);
        var otherTechnician = await LoadAssigneeAsync(onHold);
        using var client = ClientFor(technician.User);
        using var otherClient = ClientFor(otherTechnician);

        using var incomplete = await client.PostJsonAsync(PathOf(job.Id), Body(Notes, true, job.Version + 4));
        using var invalid = await otherClient.PostJsonAsync(PathOf(onHold.Id), Body(Notes, true, onHold.Version + 4));

        await ApiClient.AssertProblemAsync(incomplete, HttpStatusCode.Conflict, "inspection.incomplete");
        await ApiClient.AssertProblemAsync(invalid, HttpStatusCode.Conflict, Invalid);
    }

    [Theory]
    [InlineData("blank")]
    [InlineData("longest")]
    [InlineData("lines")]
    public async Task TechnicianNotesAreTrimmedStoredUpToTwoThousandCharactersAndBlankNotesAreStoredAsNull(string kind)
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress, answered: 10);
        using var client = ClientFor(technician.User);
        (string Sent, string? Stored) notes = kind switch
        {
            "blank" => (" \t\n ", null),
            "longest" => (new string('n', 2000), new string('n', 2000)),
            _ => ("Coil cleaned.\r\nFilters\treplaced.", "Coil cleaned.\r\nFilters\treplaced."),
        };

        using var response = await client.PostJsonAsync(PathOf(job.Id), Body(notes.Sent, true, job.Version));

        Assert.Equal(JobStatus.Completed, (await ReadJobAsync(response)).Status);
        await using var db = Fixture.CreateAppDbContext();
        var inspection = await db.Inspections.AsNoTracking().SingleAsync(entry => entry.JobId == job.Id, CancellationToken);
        Assert.Equal(notes.Stored, inspection.TechnicianNotes);
    }

    [Theory]
    [InlineData("""{}""", "siteSafetyConfirmed,version")]
    [InlineData("""{"siteSafetyConfirmed":true}""", "version")]
    [InlineData("""{"siteSafetyConfirmed":true,"version":"1"}""", "version")]
    [InlineData("""{"siteSafetyConfirmed":true,"version":null}""", "version")]
    [InlineData("""{"siteSafetyConfirmed":"true","version":@v}""", "siteSafetyConfirmed")]
    [InlineData("""{"siteSafetyConfirmed":1,"version":@v}""", "siteSafetyConfirmed")]
    [InlineData("""{"technicianNotes":42,"siteSafetyConfirmed":true,"version":@v}""", "technicianNotes")]
    [InlineData("""{"technicianNotes":"a\u0000b","siteSafetyConfirmed":true,"version":@v}""", "technicianNotes")]
    [InlineData("""{"technicianNotes":"@long","siteSafetyConfirmed":true,"version":@v}""", "technicianNotes")]
    [InlineData("""[]""", "body")]
    [InlineData("""not json""", "body")]
    public async Task AMissingOrMalformedMemberReturnsAFieldErrorKeyedByItsPathAndWritesNothing(string template, string keys)
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress, answered: 10);
        var state = await StateOfAsync(job.Id);
        using var client = ClientFor(technician.User);
        var body = SaveInspectionTests.Expand(template, job).Replace("@long", new string('n', 2001), StringComparison.Ordinal);

        using var response = await client.PostAsync(PathOf(job.Id), Json(body));

        await ApiClient.AssertFieldErrorsAsync(response, keys.Split(','));
        Assert.Equal(state, await StateOfAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task ARolledBackSubmissionReturns500WritesNothingAndRecordsNoEvent()
    {
        var refusal = new CommitRefusal();
        await using var factory = Fixture.CreateFactory(factory => factory.TestServices = services =>
            services.ConfigureDbContext<FieldServiceDbContext>(options => options.AddInterceptors(refusal)));
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress, answered: 10);
        var state = await StateOfAsync(job.Id);
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, technician.User);

        using var response = await client.PostJsonAsync(PathOf(job.Id), Body(Notes, true, job.Version));

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.InternalServerError, "server.error");
        Assert.Equal(1, refusal.Attempts);
        Assert.Equal(state, await StateOfAsync(job.Id));
        factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task AfterSubmissionTheJobAndItsInspectionAreReadOnly()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress, answered: 10);
        using var client = ClientFor(technician.User);
        using var submitted = await client.PostJsonAsync(PathOf(job.Id), Body(Notes, true, job.Version));
        var details = await ReadJobAsync(submitted);
        var state = await StateOfAsync(job.Id);

        using var hold = await client.PutJsonAsync(UpdateJobStatusTests.PathOf(job.Id), UpdateJobStatusTests.Body(JobStatus.OnHold, TestData.HoldNote, details.Version));
        using var save = await client.PutJsonAsync(
            SaveInspectionTests.PathOf(job.Id),
            SaveInspectionTests.Body(details.Version, SaveInspectionTests.Answer(5, "Fail", "Late finding")));
        using var read = await client.GetAsync(SaveInspectionTests.PathOf(job.Id));

        await ApiClient.AssertProblemAsync(hold, HttpStatusCode.Conflict, Invalid);
        await ApiClient.AssertProblemAsync(save, HttpStatusCode.Conflict, "inspection.locked");
        var inspection = await read.Content.ReadFromJsonAsync<InspectionResponse>(ApiClient.Json, CancellationToken);
        Assert.False(inspection!.Affordances.CanEdit);
        Assert.Equal(Now, inspection.SubmittedAt);
        Assert.Equal(Notes, inspection.TechnicianNotes);
        Assert.Equal(state, await StateOfAsync(job.Id));
        Assert.Single(Fixture.Factory.Events.Events);
    }

    [Fact]
    public async Task AnotherTechniciansJobAnUnassignedJobAndAMissingJobReturnTheSame404AndNothingIsWritten()
    {
        var (_, job) = await ArrangeAsync(JobStatus.InProgress, answered: 10);
        var (_, unassigned) = await ArrangeAsync(JobStatus.Unassigned);
        var other = await DataAt(Earlier).CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var state = await StateOfAsync(job.Id);
        using var client = ClientFor(other.User);

        using var forOther = await client.PostJsonAsync(PathOf(job.Id), Body(Notes, true, job.Version));
        using var forUnassigned = await client.PostJsonAsync(PathOf(unassigned.Id), Body(Notes, true, unassigned.Version));
        using var forMissing = await client.PostJsonAsync(PathOf(Guid.CreateVersion7()), Body(Notes, true, 1));

        using var first = await ApiClient.AssertProblemAsync(forOther, HttpStatusCode.NotFound, "job.not-found");
        using var second = await ApiClient.AssertProblemAsync(forUnassigned, HttpStatusCode.NotFound, "job.not-found");
        using var third = await ApiClient.AssertProblemAsync(forMissing, HttpStatusCode.NotFound, "job.not-found");
        Assert.Equal(
            third.RootElement.EnumerateObject().Select(member => member.Name),
            first.RootElement.EnumerateObject().Select(member => member.Name));
        Assert.False(second.RootElement.TryGetProperty("detail", out _));
        Assert.Equal(state, await StateOfAsync(job.Id));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task AnAdministratorGets403EvenWithAnInvalidBodyAndNothingIsWritten()
    {
        var (_, job) = await ArrangeAsync(JobStatus.InProgress, answered: 10);
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var state = await StateOfAsync(job.Id);
        using var client = ClientFor(administrator);

        using var valid = await client.PostJsonAsync(PathOf(job.Id), Body(Notes, true, job.Version));
        using var invalid = await client.PostJsonAsync(PathOf(job.Id), new { });

        await ApiClient.AssertProblemAsync(valid, HttpStatusCode.Forbidden, "auth.forbidden");
        await ApiClient.AssertProblemAsync(invalid, HttpStatusCode.Forbidden, "auth.forbidden");
        Assert.Equal(state, await StateOfAsync(job.Id));
    }

    [Fact]
    public async Task AnAnonymousCallerAnExpiredTokenAndATokenSignedWithAnotherKeyGet401AndNothingIsWritten()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress, answered: 10);
        var state = await StateOfAsync(job.Id);
        await using var expiring = Fixture.CreateFactory();
        await using var foreign = Fixture.CreateFactory();
        using var anonymous = Fixture.Factory.CreateApiClient();
        using var expired = expiring.CreateApiClient();
        expired.AccessToken = TestTokens.For(expiring, technician.User);
        expiring.Clock.Advance(TimeSpan.FromMinutes(16));
        using var signedElsewhere = Fixture.Factory.CreateApiClient();
        signedElsewhere.AccessToken = TestTokens.For(foreign, technician.User);

        using var withoutToken = await anonymous.PostJsonAsync(PathOf(job.Id), Body(Notes, true, job.Version));
        using var withExpired = await expired.PostJsonAsync(PathOf(job.Id), Body(Notes, true, job.Version));
        using var withForeign = await signedElsewhere.PostJsonAsync(PathOf(job.Id), Body(Notes, true, job.Version));

        await ApiClient.AssertProblemAsync(withoutToken, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        await ApiClient.AssertProblemAsync(withExpired, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        await ApiClient.AssertProblemAsync(withForeign, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        Assert.Equal(state, await StateOfAsync(job.Id));
        expiring.Events.AssertEmpty();
    }

    [Fact]
    public async Task AJobIdThatIsNotAGuidMatchesNoRouteAMissingBodyGets400AndAnotherMediaTypeGets415()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress, answered: 10);
        using var client = ClientFor(technician.User);

        using var notAGuid = await client.PostJsonAsync("/api/jobs/1042/submission", Body(Notes, true, job.Version));
        using var missing = await client.PostAsync(PathOf(job.Id));
        using var text = await client.PostAsync(
            PathOf(job.Id),
            new StringContent(
                string.Create(CultureInfo.InvariantCulture, $$"""{"siteSafetyConfirmed":true,"version":{{job.Version}}}"""),
                Encoding.UTF8,
                "text/plain"));

        await ApiClient.AssertProblemAsync(notAGuid, HttpStatusCode.NotFound, "resource.not-found");
        await ApiClient.AssertFieldErrorsAsync(missing, "body");
        await ApiClient.AssertProblemAsync(text, HttpStatusCode.UnsupportedMediaType, "request.unsupported-media-type");
        Assert.Equal(JobStatus.InProgress, (await LoadJobAsync(job.Id)).Status);
    }

    [Fact]
    public async Task SubmittingLogsNoTechnicianNotesNameEmailOrJobTextAtInformationLevel()
    {
        var (technician, job) = await ArrangeAsync(JobStatus.InProgress, answered: 10);
        const string Sentinel = "Replaced the Harbor Supply contactor and reset the breaker";
        await using var factory = Fixture.CreateFactory(factory =>
        {
            factory.Settings["Serilog:MinimumLevel:Default"] = "Information";
            factory.Settings["Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore"] = "Information";
        });
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, technician.User);

        using var response = await client.PostJsonAsync(PathOf(job.Id), Body(Sentinel, true, job.Version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(factory.LogSink.Events);
        foreach (var text in new[]
        {
            Sentinel, "Harbor Supply", technician.User.DisplayName, technician.User.Email, technician.PhoneNumber,
            job.Title, job.CustomerName, job.SiteAddress,
        })
        {
            Assert.All(factory.LogSink.Events, logEvent =>
                Assert.DoesNotContain(text, CapturingLogSink.Text(logEvent), StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task AfterTheDemoSeedMarcoReyesFinishesTheJob1042ChecklistAndSubmitsItAndDanaReadsTheReport()
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
            var seeded = await db.Jobs.AsNoTracking().SingleAsync(job => job.Number == 1042, CancellationToken);
            using var technicianClient = factory.CreateApiClient();
            technicianClient.AccessToken = TestTokens.For(factory, marco);
            using var administratorClient = factory.CreateApiClient();
            administratorClient.AccessToken = TestTokens.For(factory, dana);
            var supplyAir = ChecklistTemplateSeed.ReadingFieldId(2, 8, 1);

            using var opened = await technicianClient.GetAsync(SaveInspectionTests.PathOf(seeded.Id));
            var draft = await ReadInspectionAsync(opened);
            using var saved = await technicianClient.PutJsonAsync(
                SaveInspectionTests.PathOf(seeded.Id),
                SaveInspectionTests.Body(
                    draft.Version,
                    SaveInspectionTests.Answer(7, "Pass"),
                    SaveInspectionTests.Answer(8, "Pass", null, SaveInspectionTests.Reading(supplyAir, 54m)),
                    SaveInspectionTests.Answer(9, "Pass"),
                    SaveInspectionTests.Answer(10, "Pass")));
            var afterSave = await ReadInspectionAsync(saved);
            factory.Clock.Advance(TimeSpan.FromMinutes(2));
            technicianClient.AccessToken = TestTokens.For(factory, marco);
            using var reopened = await technicianClient.GetAsync(SaveInspectionTests.PathOf(seeded.Id));
            var review = await ReadInspectionAsync(reopened);
            using var submitted = await technicianClient.PostJsonAsync(PathOf(seeded.Id), Body(Notes, true, review.Version));
            var completed = await ReadJobAsync(submitted);
            administratorClient.AccessToken = TestTokens.For(factory, dana);
            using var report = await administratorClient.GetAsync(SaveInspectionTests.PathOf(seeded.Id));
            var final = await ReadInspectionAsync(report);

            Assert.Equal(JobStatus.InProgress, draft.JobStatus);
            Assert.Equal(new InspectionProgress(6, 10, 4, 1, 1), draft.Progress);
            Assert.Equal(new SubmitAffordance(false, "inspection.incomplete", 6, 10), draft.Affordances.CanSubmit);
            Assert.Equal([new InspectionReadingResponse(ChecklistTemplateSeed.ReadingFieldId(2, 3, 1), 118m), new InspectionReadingResponse(ChecklistTemplateSeed.ReadingFieldId(2, 3, 2), 285m)], draft.Items[2].Answer!.Readings);
            Assert.Equal(new SubmitAffordance(true, null, 10, 10), afterSave.Affordances.CanSubmit);
            Assert.Equal(afterSave.Version, review.Version);
            Assert.Equal(new InspectionProgress(10, 10, 8, 1, 1), review.Progress);
            Assert.Equal([new InspectionReadingResponse(supplyAir, 54m)], review.Items[7].Answer!.Readings);
            Assert.Equal(
                "Bearing noise at start-up. Recommend motor replacement within 30 days.",
                review.Items[4].Answer!.Note);
            Assert.Equal(JobStatus.Completed, completed.Status);
            Assert.Equal(JobHistoryAction.Completed, completed.History[^1].Action);
            Assert.Equal(new UserReference(marco.Id, "Marco Reyes"), completed.History[^1].Actor);
            Assert.Equal(JobStatus.Completed, final.JobStatus);
            Assert.Equal(completed.CompletedAt, final.SubmittedAt);
            Assert.Equal(completed.CompletedAt, final.CompletedAt);
            Assert.Equal(seeded.StartedAt, final.StartedAt);
            Assert.True(final.CompletedAt > final.StartedAt);
            Assert.Equal(Notes, final.TechnicianNotes);
            Assert.Equal(new InspectionProgress(10, 10, 8, 1, 1), final.Progress);
            Assert.False(final.Affordances.CanEdit);
            var recorded = Assert.Single(factory.Events.Events, jobEvent => jobEvent.JobId == seeded.Id);
            Assert.Equal(JobEventTypes.StatusChanged, recorded.Type);
            Assert.Equal(JobStatus.Completed, recorded.Status);
            Assert.Equal(JobStatus.InProgress, recorded.PreviousStatus);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    public static object Body(string? technicianNotes, bool siteSafetyConfirmed, uint version) =>
        new { technicianNotes, siteSafetyConfirmed, version };

    public static string PathOf(Guid jobId) => $"/api/jobs/{jobId}/submission";

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task<JobDetailsResponse> ReadJobAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var job = await response.Content.ReadFromJsonAsync<JobDetailsResponse>(ApiClient.Json, TestContext.Current.CancellationToken);
        return job ?? throw new InvalidOperationException("The response has no job body.");
    }

    private static async Task<InspectionResponse> ReadInspectionAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var inspection = await response.Content.ReadFromJsonAsync<InspectionResponse>(ApiClient.Json, TestContext.Current.CancellationToken);
        return inspection ?? throw new InvalidOperationException("The response has no inspection body.");
    }

    private static async Task<IReadOnlyList<string?>> MessagesAsync(HttpResponseMessage response, string key)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return [.. problem.RootElement.GetProperty("errors").GetProperty(key).EnumerateArray().Select(message => message.GetString())];
    }

    private async Task<(Technician Technician, Job Job)> ArrangeAsync(JobStatus status, int answered = 0)
    {
        var administrator = await DataAt(Earlier).CreateAdministratorAsync(CancellationToken);
        var technician = await DataAt(Earlier).CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var created = await DataAt(Earlier).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        if (answered == 0 || status is not (JobStatus.InProgress or JobStatus.OnHold))
        {
            return (technician, await DataAt(Earlier.AddMinutes(5)).MoveToAsync(created, technician, status, CancellationToken));
        }

        var job = await DataAt(Earlier.AddMinutes(5)).MoveToAsync(created, technician, JobStatus.InProgress, CancellationToken);
        await DataAt(Earlier.AddMinutes(10)).SaveInspectionAsync(
            job,
            TestData.PassingAnswers(TestData.ChecklistOf(_templateId), answered),
            CancellationToken);
        if (status == JobStatus.OnHold)
        {
            await DataAt(Earlier.AddMinutes(15)).ChangeStatusAsync(job, JobStatus.OnHold, CancellationToken, TestData.HoldNote);
        }

        return (technician, await LoadJobAsync(job.Id));
    }

    private async Task<User> LoadAssigneeAsync(Job job)
    {
        await using var db = Fixture.CreateAppDbContext();
        return await db.Users.AsNoTracking().SingleAsync(user => user.Id == job.AssignedTechnicianId, CancellationToken);
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
        var inspection = await db.Inspections.AsNoTracking().WithAnswersAndReadings().SingleOrDefaultAsync(entry => entry.JobId == jobId, CancellationToken);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{job.Version}|{job.Status}|{job.UpdatedAt:O}|{job.CompletedAt:O}|{history}|{inspection?.UpdatedAt:O}|{inspection?.SubmittedAt:O}|{inspection?.TechnicianNotes}|{inspection?.Answers.Count}");
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
            throw new InvalidOperationException("The commit is refused so the submission rolls back.");
        }
    }
}
