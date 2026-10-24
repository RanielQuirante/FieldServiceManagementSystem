using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldService.Api.Domain;
using FieldService.Api.Features;
using FieldService.Api.Features.Dashboard.GetDashboardSummary;
using FieldService.Api.Features.Jobs;
using FieldService.Api.Persistence;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Shared.Errors;
using FieldService.Api.Tests.Features.Inspections;
using FieldService.Api.Tests.Features.Jobs;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Serilog.Events;

namespace FieldService.Api.Tests.Features.Dashboard;

[Collection(DatabaseCollection.Name)]
public sealed class GetDashboardSummaryTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string SummaryPath = "/api/dashboard/summary";

    private const string DayStart = "2026-10-19T04:00:00.000Z";

    private const string DayEnd = "2026-10-20T04:00:00.000Z";

    private const string CommandExecuted = "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted";

    private const string TransactionStarted = "Microsoft.EntityFrameworkCore.Database.Transaction.TransactionStarted";

    private const string TransactionCommitted = "Microsoft.EntityFrameworkCore.Database.Transaction.TransactionCommitted";

    private const string DemoDayExample = """
        {
          "generatedAt": "2026-10-19T14:42:03.517Z",
          "openJobs": {
            "total": 17,
            "unassigned": 4,
            "assigned": 8,
            "inProgress": 2,
            "onHold": 3,
            "withTechnician": 13,
            "inProgressTechnicianCount": 2,
            "nextAssignedStart": "2026-10-19T15:30:00.000Z",
            "onHoldSince": "2026-10-16T17:50:00.000Z"
          },
          "range": {
            "from": "2026-10-19T04:00:00.000Z",
            "to": "2026-10-20T04:00:00.000Z",
            "created": 5,
            "completed": 2,
            "cancelled": 0,
            "unassignedScheduled": 2,
            "lastCompletedAt": "2026-10-19T13:58:00.000Z"
          },
          "technicians": { "total": 6, "onShift": 5 },
          "needsTechnician": [
            { "id": "job:1052", "number": 1052, "title": "Fire alarm panel trouble signal", "priority": "Urgent", "customerName": "Cedar Ridge Elementary School", "siteAddress": "1180 Cedar Ridge Drive", "scheduledStart": "2026-10-19T18:00:00.000Z", "scheduledEnd": "2026-10-19T19:30:00.000Z" },
            { "id": "job:1051", "number": 1051, "title": "Replace faulty breaker panel", "priority": "High", "customerName": "Pinecrest Public Library", "siteAddress": "300 Pinecrest Road", "scheduledStart": "2026-10-19T19:30:00.000Z", "scheduledEnd": "2026-10-19T21:00:00.000Z" },
            { "id": "job:1050", "number": 1050, "title": "Emergency lighting test", "priority": "Low", "customerName": "Northgate Logistics Warehouse", "siteAddress": "42 Industrial Parkway, Dock 4", "scheduledStart": "2026-10-21T13:00:00.000Z", "scheduledEnd": "2026-10-21T15:00:00.000Z" },
            { "id": "job:1054", "number": 1054, "title": "Leaking rooftop drain", "priority": "Normal", "customerName": "Harborview Apartments", "siteAddress": "600 Bayfront Avenue", "scheduledStart": "2026-10-22T14:00:00.000Z", "scheduledEnd": "2026-10-22T16:00:00.000Z" }
          ],
          "recentActivity": [
            { "id": "history", "occurredAt": "2026-10-19T14:42:00.000Z", "action": "Started", "jobId": "job:1049", "jobNumber": 1049, "fromStatus": "Assigned", "toStatus": "InProgress", "actor": { "id": "user:Priya Natarajan", "displayName": "Priya Natarajan" }, "technician": null, "note": null },
            { "id": "history", "occurredAt": "2026-10-19T14:31:00.000Z", "action": "PutOnHold", "jobId": "job:1044", "jobNumber": 1044, "fromStatus": "InProgress", "toStatus": "OnHold", "actor": { "id": "user:Tom Becker", "displayName": "Tom Becker" }, "technician": null, "note": "Waiting for replacement gas valve" },
            { "id": "history", "occurredAt": "2026-10-19T14:20:00.000Z", "action": "Created", "jobId": "job:1051", "jobNumber": 1051, "fromStatus": null, "toStatus": "Unassigned", "actor": { "id": "user:Dana Whitfield", "displayName": "Dana Whitfield" }, "technician": null, "note": null },
            { "id": "history", "occurredAt": "2026-10-19T14:05:00.000Z", "action": "Assigned", "jobId": "job:1047", "jobNumber": 1047, "fromStatus": "Unassigned", "toStatus": "Assigned", "actor": { "id": "user:Dana Whitfield", "displayName": "Dana Whitfield" }, "technician": { "id": "user:Marco Reyes", "displayName": "Marco Reyes" }, "note": null },
            { "id": "history", "occurredAt": "2026-10-19T13:58:00.000Z", "action": "Completed", "jobId": "job:1043", "jobNumber": 1043, "fromStatus": "InProgress", "toStatus": "Completed", "actor": { "id": "user:Aisha Karim", "displayName": "Aisha Karim" }, "technician": null, "note": null },
            { "id": "history", "occurredAt": "2026-10-19T13:12:00.000Z", "action": "Started", "jobId": "job:1042", "jobNumber": 1042, "fromStatus": "Assigned", "toStatus": "InProgress", "actor": { "id": "user:Marco Reyes", "displayName": "Marco Reyes" }, "technician": null, "note": null }
          ]
        }
        """;

    private static readonly Guid _templateId = ChecklistTemplateSeed.TemplateId(2);

    private static readonly DateTimeOffset _demoReadAt = new(2026, 10, 19, 14, 42, 3, 517, TimeSpan.Zero);

    private DateTimeOffset Now => UtcInstant.Truncate(Fixture.Factory.Clock.GetUtcNow());

    [Theory]
    [InlineData("", "from", "to")]
    [InlineData("to=" + DayEnd, "from")]
    [InlineData("from=" + DayStart, "to")]
    [InlineData("unknown=" + DayStart, "from", "to")]
    public async Task AMissingFromOrToIsAFieldErrorKeyedByTheParameter(string query, params string[] keys)
    {
        using var client = await AdministratorClientAsync();

        using var response = await client.GetAsync($"{SummaryPath}?{query}");

        await ApiClient.AssertFieldErrorsAsync(response, keys);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var errors = problem.RootElement.GetProperty("errors");
        if (errors.TryGetProperty("from", out var from))
        {
            Assert.Equal("Send the start of the window.", Assert.Single(from.EnumerateArray()).GetString());
        }

        if (errors.TryGetProperty("to", out var to))
        {
            Assert.Equal("Send the end of the window.", Assert.Single(to.EnumerateArray()).GetString());
        }
    }

    [Theory]
    [InlineData("2026-10-19T04:00:00", DayEnd, "from")]
    [InlineData("2026-10-19T04:00:00.000", DayEnd, "from")]
    [InlineData(DayStart, "2026-10-20", "to")]
    [InlineData(DayStart, "2026-10-20T04:00Z", "to")]
    [InlineData("2026-10-19T04:00:00.12345678Z", DayEnd, "from")]
    [InlineData("2026-10-19T04:00:00+0000", DayEnd, "from")]
    [InlineData("2026-02-30T04:00:00Z", DayEnd, "from")]
    [InlineData("1792420920", "1792507320", "from", "to")]
    [InlineData("yesterday", "today", "from", "to")]
    public async Task AnInstantWithoutAnOffsetOrInAnotherFormatIsAFieldErrorKeyedByTheParameter(
        string from,
        string to,
        params string[] keys)
    {
        using var client = await AdministratorClientAsync();

        using var response = await client.GetAsync(PathFor(from, to));

        await ApiClient.AssertFieldErrorsAsync(response, keys);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        foreach (var key in keys)
        {
            Assert.Equal(
                QueryParameterRule.InstantMessage,
                Assert.Single(problem.RootElement.GetProperty("errors").GetProperty(key).EnumerateArray()).GetString());
        }
    }

    [Fact]
    public async Task AnOffsetWithAPlusSignThatIsNotPercentEncodedIsAFieldError()
    {
        using var client = await AdministratorClientAsync();

        using var response = await client.GetAsync($"{SummaryPath}?from=2026-10-19T08:00:00+04:00&to={Uri.EscapeDataString(DayEnd)}");

        await ApiClient.AssertFieldErrorsAsync(response, "from");
    }

    [Theory]
    [InlineData("from=&to=", "from", "to")]
    [InlineData("from=&to=" + DayEnd, "from")]
    [InlineData("from=" + DayStart + "&from=" + DayStart + "&to=" + DayEnd, "from")]
    [InlineData("from=" + DayStart + "&to=" + DayEnd + "&to=" + DayEnd, "to")]
    public async Task AnEmptyOrRepeatedFromOrToIsAFieldErrorKeyedByTheParameter(string query, params string[] keys)
    {
        using var client = await AdministratorClientAsync();

        using var response = await client.GetAsync($"{SummaryPath}?{query}");

        await ApiClient.AssertFieldErrorsAsync(response, keys);
    }

    [Theory]
    [InlineData(DayStart, DayStart, GetDashboardSummaryRequest.ToNotAfterFromMessage)]
    [InlineData(DayEnd, DayStart, GetDashboardSummaryRequest.ToNotAfterFromMessage)]
    [InlineData("2026-10-19T00:00:00-04:00", DayStart, GetDashboardSummaryRequest.ToNotAfterFromMessage)]
    [InlineData("2026-10-19T04:00:00.0001Z", "2026-10-19T04:00:00.0009Z", GetDashboardSummaryRequest.ToNotAfterFromMessage)]
    [InlineData("2026-10-01T04:00:00.000Z", "2026-11-01T04:00:00.001Z", GetDashboardSummaryRequest.WindowTooLongMessage)]
    [InlineData("2026-01-01T00:00:00.000Z", "2026-12-31T00:00:00.000Z", GetDashboardSummaryRequest.WindowTooLongMessage)]
    public async Task AToThatIsNotAfterFromOrAWindowOverThirtyOneDaysIsAFieldErrorKeyedTo(
        string from,
        string to,
        string message)
    {
        using var client = await AdministratorClientAsync();

        using var response = await client.GetAsync(PathFor(from, to));

        await ApiClient.AssertFieldErrorsAsync(response, "to");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(message, Assert.Single(problem.RootElement.GetProperty("errors").GetProperty("to").EnumerateArray()).GetString());
    }

    [Theory]
    [InlineData("2026-10-19T04:00:00.000Z", "2026-10-19T04:00:00.001Z")]
    [InlineData("2026-10-01T04:00:00.000Z", "2026-11-01T04:00:00.000Z")]
    [InlineData("2026-10-01T00:00:00.000-04:00", "2026-11-01T04:00:00.0009Z")]
    [InlineData("2026-10-19T00:00:00-04:00", "2026-10-20T00:00:00-04:00")]
    public void AWindowFromOneMillisecondToThirtyOneDaysPassesTheWindowRule(string from, string to)
    {
        var request = new GetDashboardSummaryRequest
        {
            From = DateTimeOffset.Parse(from, CultureInfo.InvariantCulture),
            To = DateTimeOffset.Parse(to, CultureInfo.InvariantCulture),
        };
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);

        Assert.True(valid, string.Join("; ", results.Select(result => result.ErrorMessage)));
    }

    [Theory]
    [InlineData("2026-10-19T04:00:00.000Z", "2026-10-19T04:00:00.001Z")]
    [InlineData("2026-10-01T04:00:00.000Z", "2026-11-01T04:00:00.000Z")]
    public async Task TheShortestAndTheLongestWindowReturn200(string from, string to)
    {
        using var client = await AdministratorClientAsync();

        var summary = await SummaryAsync(client, PathFor(from, to));

        Assert.Equal(DateTimeOffset.Parse(from, CultureInfo.InvariantCulture), summary.Range.From);
        Assert.Equal(DateTimeOffset.Parse(to, CultureInfo.InvariantCulture), summary.Range.To);
    }

    [Fact]
    public async Task AFieldErrorNeverEchoesTheValueSentAndTheInstanceHasNoQueryString()
    {
        using var client = await AdministratorClientAsync();
        var marker = $"marker{Guid.NewGuid():N}";

        using var response = await client.GetAsync($"{SummaryPath}?from={marker}&to={marker}");

        var text = await response.Content.ReadAsStringAsync(CancellationToken);
        await ApiClient.AssertFieldErrorsAsync(response, "from", "to");
        Assert.DoesNotContain(marker, text, StringComparison.Ordinal);
        using var problem = JsonDocument.Parse(text);
        Assert.Equal(SummaryPath, problem.RootElement.GetProperty("instance").GetString());
    }

    [Fact]
    public async Task AnAnonymousCallerGets401()
    {
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.GetAsync(PathFor(DayStart, DayEnd));

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated");
    }

    [Fact]
    public async Task AnExpiredTokenAndATokenSignedWithAnotherKeyGet401()
    {
        var administrator = await new TestData(Fixture, Now).CreateAdministratorAsync(CancellationToken);
        await using var expiring = Fixture.CreateFactory();
        await using var foreign = Fixture.CreateFactory();
        using var expired = expiring.CreateApiClient();
        expired.AccessToken = TestTokens.For(expiring, administrator);
        expiring.Clock.Advance(TimeSpan.FromMinutes(16));
        using var signedElsewhere = Fixture.Factory.CreateApiClient();
        signedElsewhere.AccessToken = TestTokens.For(foreign, administrator);

        using var withExpired = await expired.GetAsync(PathFor(DayStart, DayEnd));
        using var withForeign = await signedElsewhere.GetAsync(PathFor(DayStart, DayEnd));

        await ApiClient.AssertProblemAsync(withExpired, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        await ApiClient.AssertProblemAsync(withForeign, HttpStatusCode.Unauthorized, "auth.unauthenticated");
    }

    [Theory]
    [InlineData(DayStart, DayEnd)]
    [InlineData("bad", DayStart)]
    public async Task ATechnicianGets403BeforeTheQueryIsValidated(string from, string to)
    {
        var technician = await new TestData(Fixture, Now).CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        using var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TestTokens.For(Fixture.Factory, technician.User);

        using var response = await client.GetAsync(PathFor(from, to));

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.Forbidden, "auth.forbidden");
    }

    [Fact]
    public async Task TheDemoSeedReadOnTheDemoDayInNewYorkReturnsThePlanExampleFigureForFigure()
    {
        var clock = new FakeTimeProvider(_demoReadAt);
        clock.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        await using var factory = Fixture.CreateFactory(created =>
        {
            created.Clock = clock;
            created.Settings["Seed:DemoData"] = "true";
            created.Settings["Seed:DemoPassword"] = $"demo-{Guid.NewGuid():N}";
        });
        _ = factory.Services;
        await using var db = Fixture.CreateAppDbContext();
        var dana = await db.Users.AsNoTracking().SingleAsync(user => user.Role == UserRole.Administrator, CancellationToken);
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, dana);

        using var response = await client.GetAsync(PathFor(DayStart, DayEnd));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var actual = JsonNode.Parse(await response.Content.ReadAsStringAsync(CancellationToken))!;
        await ReplaceIdsAsync(actual, db);
        Assert.Equal(JsonNode.Parse(DemoDayExample)!.ToJsonString(), actual.ToJsonString());
    }

    [Fact]
    public async Task TheRangeCountsAnInstantAtFromAndNotOneAtToForCreatedCompletedCancelledAndScheduledJobs()
    {
        var from = Now.AddDays(-3).AddMilliseconds(123);
        var to = from.AddDays(1);
        var (administrator, technician) = await PeopleAsync(from.AddDays(-2));
        foreach (var createdAt in new[] { from.AddMilliseconds(-1), from, to.AddMilliseconds(-1), to })
        {
            await DataAt(createdAt).CreateJobAsync(administrator.Id, DetailsAt(to.AddDays(10)), CancellationToken);
        }

        foreach (var completedAt in new[] { from.AddMilliseconds(-1), from, to.AddMilliseconds(-1), to })
        {
            var job = await DataAt(from.AddHours(-1)).CreateJobAsync(administrator.Id, DetailsAt(to.AddDays(10)), CancellationToken);
            await DataAt(completedAt).MoveToAsync(job, technician, JobStatus.Completed, CancellationToken);
        }

        foreach (var cancelledAt in new[] { from.AddMilliseconds(-1), from, to })
        {
            var job = await DataAt(from.AddHours(-1)).CreateJobAsync(administrator.Id, DetailsAt(to.AddDays(10)), CancellationToken);
            await DataAt(cancelledAt).MoveToAsync(job, technician, JobStatus.Cancelled, CancellationToken);
        }

        foreach (var scheduledStart in new[] { from.AddMilliseconds(-1), from, to.AddMilliseconds(-1), to })
        {
            await DataAt(from.AddDays(-1)).CreateJobAsync(administrator.Id, DetailsAt(scheduledStart), CancellationToken);
        }

        using var client = ClientFor(administrator);

        var summary = await SummaryAsync(client, PathFor(from, to));

        Assert.Equal(new DashboardRange(from, to, 2, 2, 1, 2, to.AddMilliseconds(-1)), summary.Range);
        Assert.Equal(8, summary.OpenJobs.Unassigned);
        Assert.All(summary.RecentActivity, entry => Assert.InRange(entry.OccurredAt, from, to.AddMilliseconds(-1)));
    }

    [Fact]
    public async Task RecentActivityHoldsTheEntriesFromFromUpToButNotIncludingToNewestFirst()
    {
        var from = Now.AddDays(-2).AddMilliseconds(456);
        var to = from.AddHours(6);
        var (administrator, _) = await PeopleAsync(from.AddDays(-1));
        var created = new Dictionary<DateTimeOffset, Job>();
        foreach (var createdAt in new[] { from.AddMilliseconds(-1), from, to.AddMilliseconds(-1), to })
        {
            created[createdAt] = await DataAt(createdAt).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        }

        using var client = ClientFor(administrator);

        var summary = await SummaryAsync(client, PathFor(from, to));

        Assert.Equal(
            [(to.AddMilliseconds(-1), created[to.AddMilliseconds(-1)].Id), (from, created[from].Id)],
            summary.RecentActivity.Select(entry => (entry.OccurredAt, entry.JobId)));
        Assert.Equal(2, summary.Range.Created);
    }

    [Fact]
    public async Task AFromAndToWithANumericOffsetAreReadAsUtcInstantsAndEchoedInUtc()
    {
        var from = Now.AddDays(-1).AddMilliseconds(250);
        var to = from.AddDays(1);
        var (administrator, _) = await PeopleAsync(from.AddDays(-1));
        await DataAt(from).CreateJobAsync(administrator.Id, DetailsAt(from.AddHours(3)), CancellationToken);
        await DataAt(to).CreateJobAsync(administrator.Id, DetailsAt(to.AddHours(3)), CancellationToken);
        using var client = ClientFor(administrator);

        var plusEight = await SummaryAsync(client, $"{SummaryPath}?from={Local(from, 8)}%2B08:00&to={Local(to, 8)}%2B08:00");
        var minusFour = await SummaryAsync(client, $"{SummaryPath}?from={Local(from, -4)}-04:00&to={Local(to, -4)}-04:00");

        foreach (var summary in new[] { plusEight, minusFour })
        {
            Assert.Equal(new DashboardRange(from, to, 1, 0, 0, 1, null), summary.Range);
            Assert.Equal(TimeSpan.Zero, summary.Range.From.Offset);
            Assert.Single(summary.RecentActivity);
        }
    }

    [Fact]
    public async Task WithNoJobAndNoTechnicianTheSummaryReturnsZerosNullInstantsAndEmptyLists()
    {
        var administrator = await DataAt(Now).CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);
        var from = Now.AddHours(-12);
        var to = Now.AddHours(12);

        using var response = await client.GetAsync(PathFor(from, to));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var actual = JsonNode.Parse(await response.Content.ReadAsStringAsync(CancellationToken))!;
        Assert.Equal(
            JsonNode.Parse($$"""
                {
                  "generatedAt": "{{ApiClient.WireInstant(Now)}}",
                  "openJobs": { "total": 0, "unassigned": 0, "assigned": 0, "inProgress": 0, "onHold": 0, "withTechnician": 0, "inProgressTechnicianCount": 0, "nextAssignedStart": null, "onHoldSince": null },
                  "range": { "from": "{{ApiClient.WireInstant(from)}}", "to": "{{ApiClient.WireInstant(to)}}", "created": 0, "completed": 0, "cancelled": 0, "unassignedScheduled": 0, "lastCompletedAt": null },
                  "technicians": { "total": 0, "onShift": 0 },
                  "needsTechnician": [],
                  "recentActivity": []
                }
                """)!.ToJsonString(),
            actual.ToJsonString());
    }

    [Fact]
    public async Task WithOnlyFinishedJobsTheOpenFiguresAreZeroAndTheRangeStillCountsThem()
    {
        var from = Now.AddHours(-12);
        var to = Now.AddHours(12);
        var (administrator, technician) = await PeopleAsync(from.AddDays(-1));
        await DataAt(from.AddDays(-1)).CreateTechnicianAsync(Trade.Hvac, CancellationToken, isActive: false);
        var completed = await DataAt(from.AddHours(-1)).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        await DataAt(from.AddHours(2)).MoveToAsync(completed, technician, JobStatus.Completed, CancellationToken);
        var cancelled = await DataAt(from.AddHours(-1)).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        await DataAt(from.AddHours(3)).MoveToAsync(cancelled, technician, JobStatus.Cancelled, CancellationToken);
        using var client = ClientFor(administrator);

        var summary = await SummaryAsync(client, PathFor(from, to));

        Assert.Equal(new DashboardOpenJobs(0, 0, 0, 0, 0, 0, 0, null, null), summary.OpenJobs);
        Assert.Equal(new DashboardRange(from, to, 0, 1, 1, 0, from.AddHours(2)), summary.Range);
        Assert.Equal(new DashboardTechnicianCounts(1, 1), summary.Technicians);
        Assert.Empty(summary.NeedsTechnician);
    }

    [Fact]
    public async Task TechniciansCountsActiveTechniciansOnlyAndThoseOfThemOnShift()
    {
        var data = DataAt(Now.AddDays(-1));
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken, isOnShift: true);
        await data.CreateTechnicianAsync(Trade.Electrical, CancellationToken, isOnShift: true);
        await data.CreateTechnicianAsync(Trade.Plumbing, CancellationToken, isOnShift: false);
        await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken, isOnShift: true, isActive: false);
        await data.CreateTechnicianAsync(Trade.FireSafety, CancellationToken, isOnShift: false, isActive: false);
        using var client = ClientFor(administrator);

        var summary = await SummaryAsync(client, PathFor(Now.AddHours(-1), Now.AddHours(1)));

        Assert.Equal(new DashboardTechnicianCounts(3, 2), summary.Technicians);
    }

    [Fact]
    public async Task NeedsTechnicianListsTheTenEarliestScheduledUnassignedJobsThenByNumber()
    {
        var (administrator, technician) = await PeopleAsync(Now.AddDays(-1));
        var start = Now.AddDays(1);
        var later = await DataAt(Now.AddHours(-1)).CreateJobAsync(administrator.Id, DetailsAt(start), CancellationToken);
        var earlier = await DataAt(Now.AddHours(-2)).CreateJobAsync(administrator.Id, DetailsAt(start), CancellationToken);
        var expected = new List<Job> { later, earlier };
        for (var index = 1; index <= 9; index++)
        {
            expected.Add(await DataAt(Now.AddHours(-3)).CreateJobAsync(administrator.Id, DetailsAt(start.AddHours(index)), CancellationToken));
        }

        var assigned = await DataAt(Now.AddHours(-3)).CreateJobAsync(administrator.Id, DetailsAt(start.AddHours(-5)), CancellationToken);
        await DataAt(Now.AddHours(-3)).AssignJobAsync(assigned, technician, CancellationToken);
        using var client = ClientFor(administrator);

        var summary = await SummaryAsync(client, PathFor(Now.AddHours(-1), Now.AddHours(1)));

        Assert.True(later.Number < earlier.Number && later.Id.CompareTo(earlier.Id) > 0);
        Assert.Equal(expected.Take(10).Select(job => job.Id), summary.NeedsTechnician.Select(job => job.Id));
        var first = summary.NeedsTechnician[0];
        Assert.Equal(
            new DashboardUnassignedJob(later.Id, later.Number, later.Title, later.Priority, later.CustomerName, later.SiteAddress, later.ScheduledStart, later.ScheduledEnd),
            first);
        Assert.Equal(11, summary.OpenJobs.Unassigned);
        Assert.Equal(1, summary.OpenJobs.Assigned);
    }

    [Fact]
    public async Task RecentActivityReturnsTheSixNewestEntriesAndPutsCreatedAfterAssignedAtTheSameInstant()
    {
        var (administrator, technician) = await PeopleAsync(Now.AddDays(-1));
        var earlier = new List<Job>();
        for (var minutes = 70; minutes >= 10; minutes -= 10)
        {
            earlier.Add(await DataAt(Now.AddMinutes(-minutes)).CreateJobAsync(administrator.Id, _templateId, CancellationToken));
        }

        using var client = ClientFor(administrator);
        using var create = await client.PostJsonAsync("/api/jobs", new
        {
            title = "Panel trouble signal",
            description = "The panel reports a trouble signal.",
            priority = "High",
            customerName = "Test Customer",
            siteAddress = "1 Test Way",
            scheduledStart = Now.AddDays(1),
            scheduledEnd = Now.AddDays(1).AddHours(2),
            checklistTemplateId = _templateId,
            technicianId = technician.UserId,
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var job = (await create.Content.ReadFromJsonAsync<JobDetailsResponse>(ApiClient.Json, CancellationToken))!;

        var summary = await SummaryAsync(client, PathFor(Now.AddHours(-2), Now.AddHours(1)));

        Assert.Equal(8, summary.Range.Created);
        Assert.Equal(
            [
                $"{ApiClient.WireInstant(Now)} Assigned JOB-{job.Number}",
                $"{ApiClient.WireInstant(Now)} Created JOB-{job.Number}",
                .. earlier.AsEnumerable().Reverse().Take(4).Select(entry => $"{ApiClient.WireInstant(entry.CreatedAt)} Created JOB-{entry.Number}"),
            ],
            summary.RecentActivity.Select(entry => $"{ApiClient.WireInstant(entry.OccurredAt)} {entry.Action} JOB-{entry.JobNumber}"));
        var assigned = summary.RecentActivity[0];
        Assert.Equal(new UserReference(technician.UserId, technician.User.DisplayName), assigned.Technician);
        Assert.Equal(new UserReference(administrator.Id, administrator.DisplayName), assigned.Actor);
        Assert.Equal((JobStatus?)JobStatus.Unassigned, assigned.FromStatus);
        Assert.Equal(JobStatus.Assigned, assigned.ToStatus);
        Assert.Null(summary.RecentActivity[1].FromStatus);
        Assert.Null(summary.RecentActivity[1].Technician);
        Assert.Equal(job.History.Select(entry => entry.Id).Reverse(), summary.RecentActivity.Take(2).Select(entry => entry.Id));
    }

    [Fact]
    public async Task EntriesAtOneInstantComeNewestIdFirstWithTheCreatedEntryLast()
    {
        var (administrator, technician) = await PeopleAsync(Now.AddDays(-1));
        var at = Now.AddMinutes(-5);
        var jobs = new List<Job>();
        for (var index = 0; index < 3; index++)
        {
            jobs.Add(await DataAt(Now.AddHours(-1)).CreateJobAsync(administrator.Id, _templateId, CancellationToken));
        }

        foreach (var job in jobs)
        {
            await DataAt(at).AssignJobAsync(job, technician, CancellationToken);
        }

        var createdAt = await DataAt(at).CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        using var client = ClientFor(administrator);

        var summary = await SummaryAsync(client, PathFor(at, Now));

        Assert.All(summary.RecentActivity, entry => Assert.Equal(at, entry.OccurredAt));
        Assert.Equal(4, summary.RecentActivity.Count);
        Assert.Equal((JobHistoryAction.Created, createdAt.Id), (summary.RecentActivity[^1].Action, summary.RecentActivity[^1].JobId));
        var assignedIds = summary.RecentActivity.Take(3).Select(entry => entry.Id.ToString()).ToList();
        Assert.Equal(assignedIds.OrderDescending(StringComparer.Ordinal), assignedIds);
    }

    [Fact]
    public async Task OpenJobsCountEachOpenStatusTheNextAssignedStartAtOrAfterTheReadAndTheOldestCurrentHold()
    {
        var readAt = new DateTimeOffset(2026, 10, 19, 14, 42, 3, 517, TimeSpan.Zero);
        await using var factory = Fixture.CreateFactory(created => created.Clock = new FakeTimeProvider(readAt));
        var (administrator, first) = await PeopleAsync(readAt.AddDays(-2));
        var second = await DataAt(readAt.AddDays(-2)).CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        await DataAt(readAt.AddHours(-6)).CreateJobAsync(administrator.Id, DetailsAt(readAt.AddHours(4)), CancellationToken);
        foreach (var scheduledStart in new[] { readAt.AddMilliseconds(-1), readAt, readAt.AddHours(1) })
        {
            var job = await DataAt(readAt.AddHours(-6)).CreateJobAsync(administrator.Id, DetailsAt(scheduledStart), CancellationToken);
            await DataAt(readAt.AddHours(-5)).AssignJobAsync(job, first, CancellationToken);
        }

        foreach (var technician in new[] { first, first, second })
        {
            var job = await DataAt(readAt.AddHours(-6)).CreateJobAsync(administrator.Id, DetailsAt(readAt.AddHours(-1)), CancellationToken);
            await DataAt(readAt.AddHours(-5)).MoveToAsync(job, technician, JobStatus.InProgress, CancellationToken);
        }

        var resumed = await DataAt(readAt.AddHours(-6)).CreateJobAsync(administrator.Id, DetailsAt(readAt.AddHours(-5)), CancellationToken);
        resumed = await DataAt(readAt.AddHours(-5)).MoveToAsync(resumed, second, JobStatus.OnHold, CancellationToken);
        await DataAt(readAt.AddHours(-2)).ChangeStatusAsync(resumed, JobStatus.InProgress, CancellationToken);
        await DataAt(readAt.AddHours(-1)).ChangeStatusAsync(resumed, JobStatus.OnHold, CancellationToken, TestData.HoldNote);
        var heldOnce = await DataAt(readAt.AddHours(-6)).CreateJobAsync(administrator.Id, DetailsAt(readAt.AddHours(-4)), CancellationToken);
        await DataAt(readAt.AddMinutes(-150)).MoveToAsync(heldOnce, second, JobStatus.OnHold, CancellationToken);
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, administrator);

        var summary = await SummaryAsync(client, PathFor(readAt.AddHours(-12), readAt.AddHours(12)));

        Assert.Equal(readAt, summary.GeneratedAt);
        Assert.Equal(new DashboardOpenJobs(9, 1, 3, 3, 2, 8, 2, readAt, readAt.AddMinutes(-150)), summary.OpenJobs);
    }

    [Fact]
    public async Task TerminalAndOnHoldJobsKeepTheUpdatedAtOfTheirLastTransitionSoTheIndexedFiguresEqualTheContractDefinitions()
    {
        var start = UtcInstant.Truncate(Now.AddDays(-1));
        await using var factory = Fixture.CreateFactory(created => created.Clock = new FakeTimeProvider(start));
        var (administrator, technician) = await PeopleAsync(start.AddDays(-1));
        using var dispatcher = factory.CreateApiClient();
        using var device = factory.CreateApiClient();

        async Task<JobDetailsResponse> CreateAsync()
        {
            dispatcher.AccessToken = TestTokens.For(factory, administrator);
            using var response = await dispatcher.PostJsonAsync("/api/jobs", new
            {
                title = "Rooftop unit service",
                description = "Service the rooftop unit.",
                priority = "Normal",
                customerName = "Test Customer",
                siteAddress = "1 Test Way",
                scheduledStart = start.AddHours(4),
                scheduledEnd = start.AddHours(6),
                checklistTemplateId = _templateId,
                technicianId = technician.UserId,
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JobDetailsResponse>(ApiClient.Json, CancellationToken))!;
        }

        async Task<JobDetailsResponse> SendAsync(Guid jobId, bool asTechnician, Func<ApiClient, Task<HttpResponseMessage>> send, HttpStatusCode expected)
        {
            factory.Clock.Advance(TimeSpan.FromMinutes(1));
            var client = asTechnician ? device : dispatcher;
            client.AccessToken = TestTokens.For(factory, asTechnician ? technician.User : administrator);
            using (var response = await send(client))
            {
                Assert.Equal(expected, response.StatusCode);
            }

            await using var db = Fixture.CreateAppDbContext();
            var details = await JobDetailsQuery.FindAsync(db, jobId, JobActor.Administrator(administrator.Id), CancellationToken);
            return details ?? throw new InvalidOperationException("The job could not be read.");
        }

        var held = await CreateAsync();
        var current = await SendAsync(held.Id, true, client => client.PutJsonAsync(UpdateJobStatusTests.PathOf(held.Id), UpdateJobStatusTests.Body(JobStatus.InProgress, null, held.Version)), HttpStatusCode.OK);
        current = await SendAsync(held.Id, true, client => client.PutJsonAsync(UpdateJobStatusTests.PathOf(held.Id), UpdateJobStatusTests.Body(JobStatus.OnHold, TestData.HoldNote, current.Version)), HttpStatusCode.OK);
        current = await SendAsync(held.Id, true, client => client.PutJsonAsync(UpdateJobStatusTests.PathOf(held.Id), UpdateJobStatusTests.Body(JobStatus.InProgress, null, current.Version)), HttpStatusCode.OK);
        current = await SendAsync(held.Id, true, client => client.PutJsonAsync(SaveInspectionTests.PathOf(held.Id), SaveInspectionTests.Body(current.Version, SaveInspectionTests.Answer(1, "Pass"))), HttpStatusCode.OK);
        var heldAgain = await SendAsync(held.Id, true, client => client.PutJsonAsync(UpdateJobStatusTests.PathOf(held.Id), UpdateJobStatusTests.Body(JobStatus.OnHold, TestData.HoldNote, current.Version)), HttpStatusCode.OK);
        await SendAsync(held.Id, true, client => client.PutJsonAsync(SaveInspectionTests.PathOf(held.Id), SaveInspectionTests.Body(heldAgain.Version, SaveInspectionTests.Answer(2, "Pass"))), HttpStatusCode.Conflict);
        await SendAsync(held.Id, false, client => client.PutJsonAsync(AssignJobTests.PathOf(held.Id), AssignJobTests.Body(technician.UserId, heldAgain.Version)), HttpStatusCode.Conflict);

        var completed = await CreateAsync();
        current = await SendAsync(completed.Id, true, client => client.PutJsonAsync(UpdateJobStatusTests.PathOf(completed.Id), UpdateJobStatusTests.Body(JobStatus.InProgress, null, completed.Version)), HttpStatusCode.OK);
        current = await SendAsync(completed.Id, true, client => client.PutJsonAsync(SaveInspectionTests.PathOf(completed.Id), SaveInspectionTests.BodyOf(TestData.PassingAnswers(TestData.ChecklistOf(_templateId)), current.Version)), HttpStatusCode.OK);
        var submitted = await SendAsync(completed.Id, true, client => client.PostJsonAsync(SubmitJobTests.PathOf(completed.Id), SubmitJobTests.Body(null, true, current.Version)), HttpStatusCode.OK);
        await SendAsync(completed.Id, true, client => client.PutJsonAsync(SaveInspectionTests.PathOf(completed.Id), SaveInspectionTests.Body(submitted.Version, SaveInspectionTests.Answer(5, "Fail", "Late finding"))), HttpStatusCode.Conflict);
        await SendAsync(completed.Id, false, client => client.PostJsonAsync(CancelJobTests.PathOf(completed.Id), CancelJobTests.Body(TestData.CancellationReason, submitted.Version)), HttpStatusCode.Conflict);
        await SendAsync(completed.Id, true, client => client.PostJsonAsync(SubmitJobTests.PathOf(completed.Id), SubmitJobTests.Body(null, true, submitted.Version)), HttpStatusCode.OK);

        var cancelled = await CreateAsync();
        current = await SendAsync(cancelled.Id, true, client => client.PutJsonAsync(UpdateJobStatusTests.PathOf(cancelled.Id), UpdateJobStatusTests.Body(JobStatus.InProgress, null, cancelled.Version)), HttpStatusCode.OK);
        current = await SendAsync(cancelled.Id, true, client => client.PutJsonAsync(SaveInspectionTests.PathOf(cancelled.Id), SaveInspectionTests.Body(current.Version, SaveInspectionTests.Answer(1, "Pass"))), HttpStatusCode.OK);
        var withdrawn = await SendAsync(cancelled.Id, false, client => client.PostJsonAsync(CancelJobTests.PathOf(cancelled.Id), CancelJobTests.Body(TestData.CancellationReason, current.Version)), HttpStatusCode.OK);
        await SendAsync(cancelled.Id, true, client => client.PutJsonAsync(SaveInspectionTests.PathOf(cancelled.Id), SaveInspectionTests.Body(withdrawn.Version, SaveInspectionTests.Answer(2, "Pass"))), HttpStatusCode.Conflict);
        await SendAsync(cancelled.Id, false, client => client.PutJsonAsync(AssignJobTests.PathOf(cancelled.Id), AssignJobTests.Body(technician.UserId, withdrawn.Version)), HttpStatusCode.Conflict);
        await SendAsync(cancelled.Id, false, client => client.PostJsonAsync(CancelJobTests.PathOf(cancelled.Id), CancelJobTests.Body("Again.", withdrawn.Version)), HttpStatusCode.OK);
        await SendAsync(cancelled.Id, true, client => client.PutJsonAsync(UpdateJobStatusTests.PathOf(cancelled.Id), UpdateJobStatusTests.Body(JobStatus.InProgress, null, withdrawn.Version)), HttpStatusCode.Conflict);

        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        dispatcher.AccessToken = TestTokens.For(factory, administrator);
        var from = start;
        var to = UtcInstant.Truncate(factory.Clock.GetUtcNow()).AddMinutes(1);
        var summary = await SummaryAsync(dispatcher, PathFor(from, to));

        await using var check = Fixture.CreateAppDbContext();
        var jobs = await check.Jobs.AsNoTracking().ToListAsync(CancellationToken);
        var history = await check.JobHistory.AsNoTracking().ToListAsync(CancellationToken);
        var heldJob = jobs.Single(job => job.Id == held.Id);
        var completedJob = jobs.Single(job => job.Id == completed.Id);
        var cancelledJob = jobs.Single(job => job.Id == cancelled.Id);
        Assert.Equal(JobStatus.OnHold, heldJob.Status);
        Assert.Equal(history.Where(entry => entry.JobId == held.Id && entry.Action == JobHistoryAction.PutOnHold).Max(entry => entry.OccurredAt), heldJob.UpdatedAt);
        Assert.Equal(heldAgain.UpdatedAt, heldJob.UpdatedAt);
        Assert.Equal(completedJob.CompletedAt, completedJob.UpdatedAt);
        Assert.Equal(submitted.UpdatedAt, completedJob.UpdatedAt);
        Assert.Equal(cancelledJob.CancelledAt, cancelledJob.UpdatedAt);
        Assert.Equal(withdrawn.UpdatedAt, cancelledJob.UpdatedAt);
        Assert.All(jobs, job => Assert.Equal(job.CreatedAt, history.Single(entry => entry.JobId == job.Id && entry.Action == JobHistoryAction.Created).OccurredAt));
        Assert.Equal(
            new DashboardRange(
                from,
                to,
                jobs.Count(job => job.CreatedAt >= from && job.CreatedAt < to),
                jobs.Count(job => job.CompletedAt >= from && job.CompletedAt < to),
                jobs.Count(job => job.CancelledAt >= from && job.CancelledAt < to),
                0,
                jobs.Where(job => job.CompletedAt >= from && job.CompletedAt < to).Max(job => job.CompletedAt)),
            summary.Range);
        Assert.Equal(new DashboardRange(from, to, 3, 1, 1, 0, completedJob.CompletedAt), summary.Range);
        Assert.Equal(
            jobs.Where(job => job.Status == JobStatus.OnHold)
                .Min(job => history.Where(entry => entry.JobId == job.Id && entry.Action == JobHistoryAction.PutOnHold).Max(entry => entry.OccurredAt)),
            summary.OpenJobs.OnHoldSince);
    }

    [Fact]
    public async Task EveryFigureComesFromOneSnapshotSoAJobCommittedDuringTheReadAppearsOnlyInTheNextRead()
    {
        var (administrator, _) = await PeopleAsync(Now.AddDays(-1));
        var interference = new AfterFirstRead(() => DataAt(Now.AddMinutes(-1)).CreateJobAsync(administrator.Id, DetailsAt(Now.AddHours(1)), CancellationToken));
        await using var factory = Fixture.CreateFactory(created => created.TestServices = services =>
            services.ConfigureDbContext<FieldServiceDbContext>(options => options.AddInterceptors(interference)));
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, administrator);
        var path = PathFor(Now.AddHours(-2), Now.AddHours(2));
        interference.Armed = true;

        var during = await SummaryAsync(client, path);
        var after = await SummaryAsync(client, path);

        Assert.True(interference.Ran);
        Assert.Equal((0, 0, 0, 0, 0), (during.OpenJobs.Unassigned, during.Range.Created, during.Range.UnassignedScheduled, during.NeedsTechnician.Count, during.RecentActivity.Count));
        Assert.Equal((1, 1, 1, 1, 1), (after.OpenJobs.Unassigned, after.Range.Created, after.Range.UnassignedScheduled, after.NeedsTechnician.Count, after.RecentActivity.Count));
    }

    [Fact]
    public async Task TheSixReadsRunInOneReadOnlyRepeatableReadTransactionOnTheIndexedForms()
    {
        await using var factory = Fixture.CreateFactory(created =>
        {
            created.Settings["Serilog:MinimumLevel:Default"] = "Information";
            created.Settings["Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore"] = "Debug";
        });
        var administrator = await DataAt(Now).CreateAdministratorAsync(CancellationToken);
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, administrator);
        factory.LogSink.Clear();

        await SummaryAsync(client, PathFor(Now.AddHours(-12), Now.AddHours(12)));

        var database = factory.LogSink.Events
            .Where(entry => EventNameOf(entry) is CommandExecuted or TransactionStarted or TransactionCommitted)
            .ToList();
        var steps = database.Select(Describe).ToList();
        var commands = database
            .Where(entry => EventNameOf(entry) == CommandExecuted)
            .Select(entry => CapturingLogSink.Scalar(entry, "commandText")!)
            .ToList();
        Assert.Equal(["begin RepeatableRead", .. Enumerable.Repeat("command", 7), "commit"], steps);
        Assert.Equal("SET TRANSACTION READ ONLY", commands[0]);
        Assert.All(commands.Skip(1), command => Assert.StartsWith("SELECT", command, StringComparison.Ordinal));
        Assert.All(commands, command => Assert.DoesNotContain("completed_at", command, StringComparison.Ordinal));
        Assert.All(commands, command => Assert.DoesNotContain("cancelled_at", command, StringComparison.Ordinal));
        Assert.All(commands, command => Assert.DoesNotContain("created_at", command, StringComparison.Ordinal));
        AssertContainsAll(
            commands[1],
            "FROM fieldservice.jobs AS j",
            "WHERE j.status IN ('Unassigned', 'Assigned', 'InProgress', 'OnHold')",
            "count(DISTINCT j0.assigned_technician_id) FILTER (WHERE j0.status = 'InProgress')",
            "min(j0.scheduled_start) FILTER (WHERE j0.status = 'Assigned' AND j0.scheduled_start >= @generatedAt)",
            "min(j0.updated_at) FILTER (WHERE j0.status = 'OnHold')",
            "FILTER (WHERE j0.status = 'Unassigned' AND j0.scheduled_start >= @from AND j0.scheduled_start < @to)");
        AssertContainsAll(
            commands[2],
            "FROM fieldservice.jobs AS j",
            "WHERE j.status IN ('Completed', 'Cancelled') AND j.updated_at >= @from AND j.updated_at < @to",
            "max(j0.updated_at) FILTER (WHERE j0.status = 'Completed')");
        AssertContainsAll(
            commands[3],
            "FROM fieldservice.job_history AS j",
            "WHERE j.action = 'Created' AND j.occurred_at >= @from AND j.occurred_at < @to");
        AssertContainsAll(
            commands[4],
            "FROM fieldservice.jobs AS j",
            "WHERE j.status = 'Unassigned'",
            "ORDER BY j.scheduled_start, j.number");
        AssertContainsAll(
            commands[5],
            "FROM fieldservice.technicians AS t",
            "WHERE u.is_active");
        AssertContainsAll(
            commands[6],
            "FROM fieldservice.job_history AS j",
            "WHERE j.occurred_at >= @from AND j.occurred_at < @to",
            "ORDER BY j.occurred_at DESC, j.action = 'Created', j.id DESC");
    }

    private static void AssertContainsAll(string command, params string[] fragments)
    {
        foreach (var fragment in fragments)
        {
            Assert.True(command.Contains(fragment, StringComparison.Ordinal), $"Missing \"{fragment}\" in:\n{command}");
        }
    }

    private static string Describe(LogEvent entry) => EventNameOf(entry) switch
    {
        TransactionStarted => $"begin {CapturingLogSink.Scalar(entry, "isolationLevel")}",
        TransactionCommitted => "commit",
        _ => "command",
    };

    private static string? EventNameOf(LogEvent entry) =>
        entry.Properties.TryGetValue("EventId", out var eventId) && eventId is StructureValue structure
            ? structure.Properties.Where(property => property.Name == "Name").Select(property => (property.Value as ScalarValue)?.Value as string).FirstOrDefault()
            : null;

    private static string Local(DateTimeOffset instant, int offsetHours) =>
        instant.ToOffset(TimeSpan.FromHours(offsetHours)).ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture);

    private static JobDetails DetailsAt(DateTimeOffset scheduledStart) =>
        TestData.NewDetails(_templateId, scheduledStart) with
        {
            ScheduledStart = scheduledStart,
            ScheduledEnd = scheduledStart.AddHours(2),
        };

    private static string PathFor(string from, string to) =>
        $"{SummaryPath}?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";

    private static string PathFor(DateTimeOffset from, DateTimeOffset to) =>
        PathFor(ApiClient.WireInstant(from), ApiClient.WireInstant(to));

    private static async Task<DashboardSummaryResponse> SummaryAsync(ApiClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<DashboardSummaryResponse>(ApiClient.Json, TestContext.Current.CancellationToken);
        return summary ?? throw new InvalidOperationException("The response has no summary body.");
    }

    private async Task ReplaceIdsAsync(JsonNode summary, FieldServiceDbContext db)
    {
        var jobs = await db.Jobs.AsNoTracking().ToDictionaryAsync(job => job.Number, job => job.Id, CancellationToken);
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(user => user.DisplayName, user => user.Id, CancellationToken);
        var history = await db.JobHistory.AsNoTracking().ToDictionaryAsync(entry => entry.Id, CancellationToken);

        foreach (var job in summary["needsTechnician"]!.AsArray())
        {
            var number = job!["number"]!.GetValue<long>();
            Assert.Equal(jobs[number], job["id"]!.GetValue<Guid>());
            job["id"] = $"job:{number}";
        }

        foreach (var entry in summary["recentActivity"]!.AsArray())
        {
            var number = entry!["jobNumber"]!.GetValue<long>();
            var stored = history[entry["id"]!.GetValue<Guid>()];
            Assert.Equal(jobs[number], stored.JobId);
            Assert.Equal(jobs[number], entry["jobId"]!.GetValue<Guid>());
            Assert.Equal(stored.OccurredAt, DateTimeOffset.Parse(entry["occurredAt"]!.GetValue<string>(), CultureInfo.InvariantCulture));
            Assert.Equal(stored.Action.ToString(), entry["action"]!.GetValue<string>());
            entry["id"] = "history";
            entry["jobId"] = $"job:{number}";
            foreach (var member in new[] { "actor", "technician" })
            {
                if (entry[member] is JsonObject person)
                {
                    var name = person["displayName"]!.GetValue<string>();
                    Assert.Equal(users[name], person["id"]!.GetValue<Guid>());
                    person["id"] = $"user:{name}";
                }
            }
        }
    }

    private async Task<(User Administrator, Technician Technician)> PeopleAsync(DateTimeOffset at)
    {
        var data = DataAt(at);
        return (await data.CreateAdministratorAsync(CancellationToken), await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken));
    }

    private TestData DataAt(DateTimeOffset now) => new(Fixture, now);

    private ApiClient ClientFor(User user)
    {
        var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TestTokens.For(Fixture.Factory, user);
        return client;
    }

    private async Task<ApiClient> AdministratorClientAsync()
    {
        var administrator = await new TestData(Fixture, Now).CreateAdministratorAsync(CancellationToken);
        return ClientFor(administrator);
    }

    private sealed class AfterFirstRead(Func<Task> action) : DbCommandInterceptor
    {
        private int _ran;

        public bool Armed { get; set; }

        public bool Ran => Volatile.Read(ref _ran) == 1;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && Interlocked.Exchange(ref _ran, 1) == 0)
            {
                await action();
            }

            return result;
        }
    }
}
