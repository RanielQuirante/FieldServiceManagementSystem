using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldService.Api.Domain;
using FieldService.Api.Features;
using FieldService.Api.Features.Jobs;
using FieldService.Api.Features.Jobs.CreateJob;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Shared.Errors;
using FieldService.Api.Shared.Realtime;
using FieldService.Api.Tests.Infrastructure;
using FieldService.Api.Tests.Shared.Realtime;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldService.Api.Tests.Features.Jobs;

[Collection(DatabaseCollection.Name)]
public sealed class CreateJobTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string JobsPath = "/api/jobs";

    private static readonly Guid _templateId = ChecklistTemplateSeed.TemplateId(1);

    private TestData Data => new(Fixture, Fixture.Factory.Clock.GetUtcNow());

    [Fact]
    public async Task AValidCreateReturns201WithLocationNumberStatusVersionAndOneCreatedHistoryEntry()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);
        var now = UtcInstant.Truncate(Fixture.Factory.Clock.GetUtcNow());
        var body = ValidBody();

        using var response = await client.PostAsync(JobsPath, Json(body));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var job = await ReadJobAsync(response);
        Assert.Equal($"/api/jobs/{job.Id}", response.Headers.Location?.OriginalString);
        Assert.False(response.Headers.Location!.IsAbsoluteUri);
        Assert.True(job.Number >= JobNumber.First);
        Assert.Equal(JobStatus.Unassigned, job.Status);
        Assert.NotEqual(0u, job.Version);
        Assert.Equal("Exterior signage lights out", job.Title);
        Assert.Equal("Both pylon signs are dark after dusk.\nTimer and photocell suspected.", job.Description);
        Assert.Equal(JobPriority.Normal, job.Priority);
        Assert.Equal("Test Customer", job.CustomerName);
        Assert.Equal("1 Test Way", job.SiteAddress);
        Assert.Equal("Meet the site contact at the north office", job.SiteAccessNotes);
        Assert.Equal("Test Contact", job.ContactName);
        Assert.Equal("(555) 010-0001", job.ContactPhone);
        Assert.Null(job.Equipment);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 13, 0, 0, TimeSpan.Zero), job.ScheduledStart);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero), job.ScheduledEnd);
        Assert.Equal(new ChecklistTemplateReference(_templateId, "General electrical inspection", Trade.Electrical), job.ChecklistTemplate);
        Assert.Null(job.AssignedTechnician);
        Assert.Equal(new UserReference(administrator.Id, administrator.DisplayName), job.CreatedBy);
        Assert.Equal(now, job.CreatedAt);
        Assert.Equal(now, job.UpdatedAt);
        Assert.Null(job.AssignedAt);
        Assert.Null(job.StartedAt);
        Assert.Null(job.CompletedAt);
        Assert.Null(job.CancelledAt);
        var created = Assert.Single(job.History);
        Assert.Equal(JobHistoryAction.Created, created.Action);
        Assert.Null(created.FromStatus);
        Assert.Equal(JobStatus.Unassigned, created.ToStatus);
        Assert.Equal(new UserReference(administrator.Id, administrator.DisplayName), created.Actor);
        Assert.Null(created.Technician);
        Assert.Null(created.Note);
        Assert.Equal(now, created.OccurredAt);
    }

    [Fact]
    public async Task TheResponseWritesEveryMemberWithInstantsInTheWireFormatAndNullsPresent()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);
        var body = ValidBody();
        body["equipment"] = null;

        using var response = await client.PostAsync(JobsPath, Json(body));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var root = document.RootElement;
        Assert.Equal(
            [
                "id", "number", "title", "description", "priority", "status", "customerName", "siteAddress",
                "siteAccessNotes", "contactName", "contactPhone", "equipment", "scheduledStart", "scheduledEnd",
                "checklistTemplate", "assignedTechnician", "createdBy", "createdAt", "updatedAt", "assignedAt",
                "startedAt", "completedAt", "cancelledAt", "history", "version", "affordances",
            ],
            root.EnumerateObject().Select(member => member.Name));
        Assert.Equal("2026-10-20T13:00:00.000Z", root.GetProperty("scheduledStart").GetString());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", root.GetProperty("createdAt").GetString());
        Assert.Equal("Unassigned", root.GetProperty("status").GetString());
        Assert.Equal("Normal", root.GetProperty("priority").GetString());
        Assert.Equal("Electrical", root.GetProperty("checklistTemplate").GetProperty("trade").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("equipment").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("assignedTechnician").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("history")[0].GetProperty("fromStatus").ValueKind);
        Assert.Equal("Created", root.GetProperty("history")[0].GetProperty("action").GetString());
        var affordances = root.GetProperty("affordances");
        Assert.Equal(
            ["allowedStatusChanges", "canSubmit", "canAssign"],
            affordances.EnumerateObject().Select(member => member.Name));
        Assert.Equal("[]", affordances.GetProperty("allowedStatusChanges").GetRawText());
        Assert.Equal(
            """{"allowed":false,"blockingCode":"auth.forbidden","answered":0,"total":8}""",
            affordances.GetProperty("canSubmit").GetRawText());
        Assert.Equal(JsonValueKind.True, affordances.GetProperty("canAssign").ValueKind);
    }

    [Fact]
    public async Task CreatingAJobWithATechnicianYieldsAssignedWithCreatedAndAssignedEntriesAtTheSameInstant()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        var technician = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        using var client = ClientFor(administrator);
        var now = UtcInstant.Truncate(Fixture.Factory.Clock.GetUtcNow());
        var body = ValidBody();
        body["technicianId"] = technician.UserId.ToString();

        using var response = await client.PostAsync(JobsPath, Json(body));

        var job = await ReadJobAsync(response);
        Assert.Equal($"/api/jobs/{job.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(JobStatus.Assigned, job.Status);
        Assert.Equal(new JobTechnicianReference(technician.UserId, technician.User.DisplayName, Trade.Electrical), job.AssignedTechnician);
        Assert.Equal(now, job.CreatedAt);
        Assert.Equal(now, job.UpdatedAt);
        Assert.Equal(now, job.AssignedAt);
        Assert.Equal([JobHistoryAction.Created, JobHistoryAction.Assigned], job.History.Select(entry => entry.Action));
        Assert.All(job.History, entry => Assert.Equal(now, entry.OccurredAt));
        Assert.All(job.History, entry => Assert.Equal(new UserReference(administrator.Id, administrator.DisplayName), entry.Actor));
        Assert.Equal([null, JobStatus.Unassigned], job.History.Select(entry => entry.FromStatus));
        Assert.Equal([JobStatus.Unassigned, JobStatus.Assigned], job.History.Select(entry => entry.ToStatus));
        Assert.Equal([null, new UserReference(technician.UserId, technician.User.DisplayName)], job.History.Select(entry => entry.Technician));
        Assert.True(job.Affordances.CanAssign);

        await using var db = Fixture.CreateAppDbContext();
        var stored = await db.Jobs.AsNoTracking().Include(entry => entry.History).SingleAsync(entry => entry.Id == job.Id, CancellationToken);
        Assert.Equal(JobStatus.Assigned, stored.Status);
        Assert.Equal(technician.UserId, stored.AssignedTechnicianId);
        Assert.Equal(now, stored.AssignedAt);
        Assert.Equal(job.Version, stored.Version);
        Assert.Equal(
            job.History.Select(entry => entry.Id).Order(),
            stored.History.Select(entry => entry.Id).Order());
        Assert.Equal(technician.UserId, stored.History.Single(entry => entry.Action == JobHistoryAction.Assigned).TechnicianId);
    }

    [Fact]
    public async Task AJobCreatedWithATechnicianRecordsOneJobCreatedEventWithStatusAssignedAndNoJobAssignedEvent()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        var technician = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        using var client = ClientFor(administrator);
        bool? committedWhenEnqueued = null;
        Fixture.Factory.Events.OnEnqueue = jobEvent =>
        {
            using var db = Fixture.CreateAppDbContext();
            committedWhenEnqueued = db.Jobs.AsNoTracking()
                .Any(job => job.Id == jobEvent.JobId && job.AssignedTechnicianId == technician.UserId);
        };
        var body = ValidBody();
        body["technicianId"] = technician.UserId.ToString();

        using var response = await client.PostAsync(JobsPath, Json(body));

        var job = await ReadJobAsync(response);
        var recorded = Assert.Single(Fixture.Factory.Events.Events);
        Assert.True(committedWhenEnqueued);
        Assert.Equal(JobEventTypes.Created, recorded.Type);
        Assert.Equal(job.Id, recorded.JobId);
        Assert.Equal(JobStatus.Assigned, recorded.Status);
        Assert.Null(recorded.PreviousStatus);
        Assert.Equal(job.UpdatedAt, recorded.OccurredAt);
        Assert.Equal(client.LastCorrelationId, recorded.CorrelationId);
    }

    [Fact]
    public async Task AnExplicitNullTechnicianCreatesAnUnassignedJob()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);
        var body = ValidBody();
        body["technicianId"] = null;

        using var response = await client.PostAsync(JobsPath, Json(body));

        var job = await ReadJobAsync(response);
        Assert.Equal(JobStatus.Unassigned, job.Status);
        Assert.Null(job.AssignedTechnician);
        Assert.Single(job.History);
        Assert.Equal(JobStatus.Unassigned, Fixture.Factory.Events.SingleOf(JobEventTypes.Created, job.Id).Status);
    }

    [Fact]
    public async Task AnUnknownTechnicianOrAUserWhoIsNotATechnicianReturns400KeyedTechnicianIdAndWritesNothing()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);

        foreach (var technicianId in new[] { Guid.CreateVersion7(), administrator.Id, Guid.Empty })
        {
            var body = ValidBody();
            body["technicianId"] = technicianId.ToString();

            using var response = await client.PostAsync(JobsPath, Json(body));

            await ApiClient.AssertFieldErrorsAsync(response, "technicianId");
            Assert.Equal([AssigneeQuery.UnknownMessage], await MessagesAsync(response, "technicianId"));
        }

        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AnUnknownTemplateAndAnUnknownTechnicianAreReportedTogether()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);
        var body = ValidBody();
        body["checklistTemplateId"] = Guid.CreateVersion7().ToString();
        body["technicianId"] = Guid.CreateVersion7().ToString();

        using var response = await client.PostAsync(JobsPath, Json(body));

        await ApiClient.AssertFieldErrorsAsync(response, "checklistTemplateId", "technicianId");
        Assert.Equal([CreateJobEndpoint.UnknownTemplateMessage], await MessagesAsync(response, "checklistTemplateId"));
        Assert.Equal([AssigneeQuery.UnknownMessage], await MessagesAsync(response, "technicianId"));
        await AssertNothingWrittenAsync();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task AnOffShiftOrInactiveTechnicianReturns409TechnicianUnavailableAndWritesNothing(bool isOnShift, bool isActive)
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        var technician = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken, isOnShift, isActive);
        using var client = ClientFor(administrator);
        var body = ValidBody();
        body["technicianId"] = technician.UserId.ToString();

        using var response = await client.PostAsync(JobsPath, Json(body));

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Conflict, "technician.unavailable");
        Assert.Equal("The technician is not available.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.10", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal("The technician is inactive or off shift.", problem.RootElement.GetProperty("detail").GetString());
        Assert.Equal(client.LastCorrelationId, problem.RootElement.GetProperty("correlationId").GetString());
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AnUnknownTemplateIsReportedBeforeAnUnavailableTechnician()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        var technician = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken, isOnShift: false);
        using var client = ClientFor(administrator);
        var body = ValidBody();
        body["checklistTemplateId"] = Guid.CreateVersion7().ToString();
        body["technicianId"] = technician.UserId.ToString();

        using var response = await client.PostAsync(JobsPath, Json(body));

        await ApiClient.AssertFieldErrorsAsync(response, "checklistTemplateId");
        await AssertNothingWrittenAsync();
    }

    [Theory]
    [InlineData("\"not-a-uuid\"")]
    [InlineData("5")]
    [InlineData("true")]
    [InlineData("\"\"")]
    public async Task AMalformedTechnicianIdReturnsAFieldErrorKeyedTechnicianId(string technicianId)
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);
        var body = ValidBody();
        body["technicianId"] = JsonNode.Parse(technicianId);

        using var response = await client.PostAsync(JobsPath, Json(body));

        await ApiClient.AssertFieldErrorsAsync(response, "technicianId");
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task TheCreatedJobReadsBackThroughAFreshDbContextWithItsCreatedEntry()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.PostAsync(JobsPath, Json(ValidBody()));

        var job = await ReadJobAsync(response);
        await using var db = Fixture.CreateAppDbContext();
        var stored = await db.Jobs.AsNoTracking().Include(entry => entry.History).SingleAsync(entry => entry.Id == job.Id, CancellationToken);
        Assert.Equal(job.Number, stored.Number);
        Assert.Equal(job.Version, stored.Version);
        Assert.Equal(job.Title, stored.Title);
        Assert.Equal(job.Description, stored.Description);
        Assert.Equal(job.CustomerName, stored.CustomerName);
        Assert.Equal(job.SiteAddress, stored.SiteAddress);
        Assert.Equal(job.SiteAccessNotes, stored.SiteAccessNotes);
        Assert.Equal(job.ContactName, stored.ContactName);
        Assert.Equal(job.ContactPhone, stored.ContactPhone);
        Assert.Null(stored.Equipment);
        Assert.Equal(job.ScheduledStart, stored.ScheduledStart);
        Assert.Equal(job.ScheduledEnd, stored.ScheduledEnd);
        Assert.Equal(_templateId, stored.ChecklistTemplateId);
        Assert.Equal(JobStatus.Unassigned, stored.Status);
        Assert.Null(stored.AssignedTechnicianId);
        Assert.Equal(administrator.Id, stored.CreatedByUserId);
        Assert.Equal(job.CreatedAt, stored.CreatedAt);
        Assert.Equal(job.UpdatedAt, stored.UpdatedAt);
        var entry = Assert.Single(stored.History);
        Assert.Equal(job.History[0].Id, entry.Id);
        Assert.Equal(JobHistoryAction.Created, entry.Action);
        Assert.Equal(administrator.Id, entry.ActorUserId);
        Assert.Equal(stored.CreatedAt, entry.OccurredAt);
    }

    [Fact]
    public async Task TheCreatedJobIsStillListedByANewHostAfterTheFirstOneStops()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        Guid createdId;
        await using (var first = Fixture.CreateFactory())
        {
            using var client = first.CreateApiClient();
            client.AccessToken = TestTokens.For(first, administrator);
            using var response = await client.PostAsync(JobsPath, Json(ValidBody()));
            createdId = (await ReadJobAsync(response)).Id;
        }

        await using var second = Fixture.CreateFactory();
        using var restarted = second.CreateApiClient();
        restarted.AccessToken = TestTokens.For(second, administrator);

        using var list = await restarted.GetAsync(JobsPath);
        using var detail = await restarted.GetAsync($"{JobsPath}/{createdId}");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var page = JsonDocument.Parse(await list.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(
            [createdId],
            page.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
    }

    [Fact]
    public async Task CreatingAJobLogsNoTextOfTheJobAtInformationLevel()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        await using var factory = Fixture.CreateFactory(factory =>
        {
            factory.Settings["Serilog:MinimumLevel:Default"] = "Information";
            factory.Settings["Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore"] = "Information";
        });
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, administrator);
        var body = ValidBody();

        using var response = await client.PostAsync(JobsPath, Json(body));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotEmpty(factory.LogSink.Events);
        foreach (var text in new[] { "Exterior signage", "Test Customer", "1 Test Way", "Test Contact", "(555) 010-0001", "north office", administrator.DisplayName, administrator.Email })
        {
            Assert.All(factory.LogSink.Events, logEvent =>
                Assert.DoesNotContain(text, CapturingLogSink.Text(logEvent), StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task OneJobCreatedEventIsRecordedOnlyAfterTheCommit()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);
        bool? committedWhenEnqueued = null;
        Fixture.Factory.Events.OnEnqueue = jobEvent =>
        {
            using var db = Fixture.CreateAppDbContext();
            committedWhenEnqueued = db.Jobs.AsNoTracking().Any(job => job.Id == jobEvent.JobId);
        };

        using var response = await client.PostAsync(JobsPath, Json(ValidBody()));

        var job = await ReadJobAsync(response);
        var recorded = Fixture.Factory.Events.SingleOf(JobEventTypes.Created, job.Id);
        Assert.Single(Fixture.Factory.Events.Events);
        Assert.True(committedWhenEnqueued);
        Assert.Equal(JobEventTypes.Created, recorded.Type);
        Assert.Equal(JobEvent.CurrentVersion, recorded.Version);
        Assert.Equal(job.Number, recorded.JobNumber);
        Assert.Equal(JobStatus.Unassigned, recorded.Status);
        Assert.Null(recorded.PreviousStatus);
        Assert.Equal(job.UpdatedAt, recorded.OccurredAt);
        Assert.Equal(client.LastCorrelationId, recorded.CorrelationId);
    }

    [Fact]
    public async Task AfterTheDemoSeedTheFirstCreatedJobIsJob1056()
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
            using var client = factory.CreateApiClient();
            client.AccessToken = TestTokens.For(factory, dana);

            using var response = await client.PostAsync(JobsPath, Json(ValidBody()));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var job = await ReadJobAsync(response);
            Assert.Equal(1056, job.Number);
            Assert.Equal(JobStatus.Unassigned, job.Status);
            factory.Events.SingleOf(JobEventTypes.Created, job.Id);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task AnEmptySiteAddressReturnsAFieldErrorKeyedSiteAddressAndWritesNothing()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);
        var body = ValidBody();
        body["siteAddress"] = "   ";

        using var response = await client.PostAsync(JobsPath, Json(body));

        await ApiClient.AssertFieldErrorsAsync(response, "siteAddress");
        Assert.Equal(["Enter the site address."], await MessagesAsync(response, "siteAddress"));
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AnEndNotAfterTheStartReturnsAFieldErrorKeyedScheduledEndInCamelCase()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);
        var equal = ValidBody();
        equal["scheduledEnd"] = equal["scheduledStart"]!.GetValue<string>();
        var before = ValidBody();
        before["scheduledEnd"] = "2026-10-20T08:59:59-04:00";
        var sameMillisecond = ValidBody();
        sameMillisecond["scheduledStart"] = "2026-10-20T13:00:00.0001Z";
        sameMillisecond["scheduledEnd"] = "2026-10-20T13:00:00.0009Z";

        foreach (var body in new[] { equal, before, sameMillisecond })
        {
            using var response = await client.PostAsync(JobsPath, Json(body));

            await ApiClient.AssertFieldErrorsAsync(response, "scheduledEnd");
        }

        using var last = await client.PostAsync(JobsPath, Json(before));
        using var problem = JsonDocument.Parse(await last.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(
            "The scheduled end must be after the scheduled start.",
            problem.RootElement.GetProperty("errors").GetProperty("scheduledEnd")[0].GetString());
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AMissingSiteAddressAndAnEndNotAfterTheStartAreReportedTogether()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);
        var body = ValidBody();
        body.Remove("siteAddress");
        body["scheduledEnd"] = body["scheduledStart"]!.GetValue<string>();

        using var response = await client.PostAsync(JobsPath, Json(body));

        await ApiClient.AssertFieldErrorsAsync(response, "siteAddress", "scheduledEnd");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(
            ["siteAddress", "scheduledEnd"],
            problem.RootElement.GetProperty("errors").EnumerateObject().Select(member => member.Name));
        Assert.Equal(["Enter the site address."], await MessagesAsync(response, "siteAddress"));
        Assert.Equal(["The scheduled end must be after the scheduled start."], await MessagesAsync(response, "scheduledEnd"));
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AnEndNotAfterTheStartIsReportedWithEveryOtherFieldError()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);
        var body = ValidBody();
        body["title"] = new string('a', 121);
        body["customerName"] = "Tab\there";
        body["description"] = "Bell\u0007";
        body["contactPhone"] = "*67 555 0100";
        body.Remove("checklistTemplateId");
        body["scheduledEnd"] = "2026-10-20T08:59:59-04:00";

        using var response = await client.PostAsync(JobsPath, Json(body));

        await ApiClient.AssertFieldErrorsAsync(
            response,
            "title",
            "description",
            "customerName",
            "contactPhone",
            "scheduledEnd",
            "checklistTemplateId");
        Assert.Equal(["Enter at most 120 characters."], await MessagesAsync(response, "title"));
        Assert.Equal(["Remove control characters other than line breaks and tabs."], await MessagesAsync(response, "description"));
        Assert.Equal(["Remove line breaks, tabs and other control characters."], await MessagesAsync(response, "customerName"));
        Assert.Equal(["The scheduled end must be after the scheduled start."], await MessagesAsync(response, "scheduledEnd"));
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AMissingStartIsReportedAloneWithoutTheEndRule()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);
        var body = ValidBody();
        body.Remove("scheduledStart");

        using var response = await client.PostAsync(JobsPath, Json(body));

        await ApiClient.AssertFieldErrorsAsync(response, "scheduledStart");
        Assert.Equal(["Enter the scheduled start."], await MessagesAsync(response, "scheduledStart"));
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AnUnknownChecklistTemplateReturnsAFieldErrorKeyedChecklistTemplateIdAndWritesNothing()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);
        var body = ValidBody();
        body["checklistTemplateId"] = Guid.CreateVersion7().ToString();

        using var response = await client.PostAsync(JobsPath, Json(body));

        await ApiClient.AssertFieldErrorsAsync(response, "checklistTemplateId");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(
            CreateJobEndpoint.UnknownTemplateMessage,
            problem.RootElement.GetProperty("errors").GetProperty("checklistTemplateId")[0].GetString());
        Assert.Equal("One or more fields are invalid.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.1", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(client.LastCorrelationId, problem.RootElement.GetProperty("correlationId").GetString());
        await AssertNothingWrittenAsync();
    }

    [Theory]
    [InlineData("1")]
    [InlineData("\"Critical\"")]
    [InlineData("\"\"")]
    [InlineData("null")]
    [InlineData("true")]
    public async Task AWrongTypedOrUnknownPriorityReturnsAFieldErrorKeyedPriority(string priority)
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);
        var body = ValidBody();
        body["priority"] = JsonNode.Parse(priority);

        using var response = await client.PostAsync(JobsPath, Json(body));

        await ApiClient.AssertFieldErrorsAsync(response, "priority");
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AnEmptyObjectNamesEveryRequiredMember()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.PostAsync(JobsPath, Json(new JsonObject()));

        await ApiClient.AssertFieldErrorsAsync(
            response,
            "title",
            "description",
            "priority",
            "customerName",
            "siteAddress",
            "scheduledStart",
            "scheduledEnd",
            "checklistTemplateId");
        Assert.Equal(["Choose the priority."], await MessagesAsync(response, "priority"));
        Assert.Equal(["Enter the scheduled start."], await MessagesAsync(response, "scheduledStart"));
        Assert.Equal(["Choose a checklist template."], await MessagesAsync(response, "checklistTemplateId"));
        await AssertNothingWrittenAsync();
    }

    [Theory]
    [InlineData("title", 121)]
    [InlineData("description", 2001)]
    [InlineData("customerName", 121)]
    [InlineData("siteAddress", 301)]
    [InlineData("siteAccessNotes", 501)]
    [InlineData("contactName", 101)]
    [InlineData("equipment", 201)]
    public async Task AStringLongerThanItsColumnReturnsAFieldErrorKeyedByTheMember(string member, int length)
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);
        var body = ValidBody();
        body[member] = new string('a', length);

        using var response = await client.PostAsync(JobsPath, Json(body));

        await ApiClient.AssertFieldErrorsAsync(response, member);
        Assert.Equal([$"Enter at most {length - 1} characters."], await MessagesAsync(response, member));
        await AssertNothingWrittenAsync();
    }

    [Theory]
    [InlineData("*67 555 0100")]
    [InlineData("555#0100")]
    [InlineData("555-0100+1")]
    [InlineData("call 555 0100")]
    [InlineData("12")]
    [InlineData("555 0100\n")]
    [InlineData("555\t0100")]
    [InlineData("555\u00850100")]
    public async Task AContactPhoneOutsideThePatternReturnsAFieldErrorKeyedContactPhone(string phone)
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);
        var body = ValidBody();
        body["contactPhone"] = phone;

        using var response = await client.PostAsync(JobsPath, Json(body));

        await ApiClient.AssertFieldErrorsAsync(response, "contactPhone");
        Assert.Equal(
            ["Enter 3 to 31 digits, spaces, parentheses, dots or hyphens, with an optional leading +."],
            await MessagesAsync(response, "contactPhone"));
        await AssertNothingWrittenAsync();
    }

    [Theory]
    [InlineData("title", "Line one\nline two")]
    [InlineData("title", "Tab\there")]
    [InlineData("customerName", "Nul\u0000")]
    [InlineData("siteAddress", "Escape\u001b")]
    [InlineData("contactName", "Delete\u007f")]
    [InlineData("equipment", "Control\u0085")]
    [InlineData("description", "Bell\u0007")]
    [InlineData("siteAccessNotes", "Nul\u0000")]
    public async Task AControlCharacterOutsideTheAllowedLineBreaksReturnsAFieldErrorKeyedByTheMember(string member, string value)
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);
        var body = ValidBody();
        body[member] = value;

        using var response = await client.PostAsync(JobsPath, Json(body));

        await ApiClient.AssertFieldErrorsAsync(response, member);
        Assert.Equal(
            [member is "description" or "siteAccessNotes" ? MultiLineAttribute.Message : SingleLineAttribute.Message],
            await MessagesAsync(response, member));
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task MultiLineMembersKeepLineFeedsCarriageReturnsAndTabs()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);
        var body = ValidBody();
        body["description"] = "First line\r\nSecond line\tindented";
        body["siteAccessNotes"] = "Gate code at reception\nRing twice";

        using var response = await client.PostAsync(JobsPath, Json(body));

        var job = await ReadJobAsync(response);
        Assert.Equal("First line\r\nSecond line\tindented", job.Description);
        Assert.Equal("Gate code at reception\nRing twice", job.SiteAccessNotes);
    }

    [Theory]
    [InlineData("scheduledStart", "2026-10-20T09:00:00")]
    [InlineData("scheduledStart", "2026-10-20")]
    [InlineData("scheduledEnd", "tomorrow")]
    [InlineData("checklistTemplateId", "not-a-uuid")]
    public async Task AMalformedValueReturnsAFieldErrorKeyedByItsJsonPath(string member, string value)
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);
        var body = ValidBody();
        body[member] = value;

        using var response = await client.PostAsync(JobsPath, Json(body));

        await ApiClient.AssertFieldErrorsAsync(response, member);
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AnAnonymousCallerGets401AndNothingIsWritten()
    {
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.PostAsync(JobsPath, Json(ValidBody()));

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task ATechnicianGets403AndNothingIsWritten()
    {
        var technician = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        using var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TestTokens.For(Fixture.Factory, technician.User);

        using var response = await client.PostAsync(JobsPath, Json(ValidBody()));

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.Forbidden, "auth.forbidden");
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task ACommittedCreateIsPostedOnceAsASignedJobCreatedEnvelopeWithTheRequestsCorrelationId()
    {
        NotificationsStub? stub = null;
        await using var factory = Fixture.CreateFactory(factory => stub = NotificationsStub.AttachTo(factory));
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, administrator);

        using var response = await client.PostAsync(JobsPath, Json(ValidBody()));
        var job = await ReadJobAsync(response);
        await stub!.WaitForRequestsAsync(1);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var webhook = Assert.Single(stub.Requests);
        NotificationsStub.AssertSignedEnvelope(
            factory,
            webhook,
            client.LastCorrelationId!,
            JobEventTypes.Created,
            job.Id,
            job.Number,
            JobStatus.Unassigned,
            null);
    }

    private static JsonObject ValidBody() => new()
    {
        ["title"] = "  Exterior signage lights out ",
        ["description"] = "Both pylon signs are dark after dusk.\nTimer and photocell suspected.",
        ["priority"] = "Normal",
        ["customerName"] = "Test Customer",
        ["siteAddress"] = "1 Test Way",
        ["siteAccessNotes"] = "Meet the site contact at the north office",
        ["contactName"] = "Test Contact",
        ["contactPhone"] = "(555) 010-0001",
        ["equipment"] = "   ",
        ["scheduledStart"] = "2026-10-20T09:00:00-04:00",
        ["scheduledEnd"] = "2026-10-20T11:00:00-04:00",
        ["checklistTemplateId"] = _templateId.ToString(),
    };

    private static StringContent Json(JsonNode body) =>
        new(body.ToJsonString(), Encoding.UTF8, "application/json");

    private static async Task<IReadOnlyList<string?>> MessagesAsync(HttpResponseMessage response, string key)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return [.. problem.RootElement.GetProperty("errors").GetProperty(key).EnumerateArray().Select(message => message.GetString())];
    }

    private static async Task<JobDetailsResponse> ReadJobAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await response.Content.ReadFromJsonAsync<JobDetailsResponse>(ApiClient.Json, TestContext.Current.CancellationToken);
        return job ?? throw new InvalidOperationException("The response has no job body.");
    }

    private ApiClient ClientFor(User user)
    {
        var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TestTokens.For(Fixture.Factory, user);
        return client;
    }

    private async Task AssertNothingWrittenAsync()
    {
        await using var db = Fixture.CreateAppDbContext();
        Assert.Equal(0, await db.Jobs.CountAsync(CancellationToken));
        Assert.Equal(0, await db.JobHistory.CountAsync(CancellationToken));
        Fixture.Factory.Events.AssertEmpty();
    }

    private async Task RestartJobNumbersAsync()
    {
        await using var migrator = await Fixture.OpenMigratorConnectionAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT setval('fieldservice.jobs_number_seq', 1001, false)", migrator);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }
}
