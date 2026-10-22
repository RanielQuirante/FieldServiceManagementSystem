using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Features.Technicians.GetAssignedJobs;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Tests.Features.Technicians;

[Collection(DatabaseCollection.Name)]
public sealed class GetAssignedJobsTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string MyJobsPath = "/api/technicians/me/jobs";

    private static readonly Guid _templateId = ChecklistTemplateSeed.TemplateId(1);

    private DateTimeOffset Now => UtcInstant.Truncate(Fixture.Factory.Clock.GetUtcNow());

    private TestData Data => new(Fixture, Now.AddDays(-2));

    [Theory]
    [InlineData("?view=active")]
    [InlineData("")]
    public async Task TheActiveViewHoldsAssignedInProgressAndOnHoldJobsOrderedByScheduledStartThenNumber(string query)
    {
        var world = await ArrangeAsync();
        using var client = ClientFor(world.Technician.User);

        using var response = await client.GetAsync(MyJobsPath + query);

        var items = await ReadItemsAsync(response);
        Assert.Equal(
            [world.OnHoldFirst.Id, world.AssignedLowNumber.Id, world.InProgressHighNumber.Id, world.AssignedLatest.Id],
            items.Select(item => item.Id));
        Assert.Equal(
            [JobStatus.OnHold, JobStatus.Assigned, JobStatus.InProgress, JobStatus.Assigned],
            items.Select(item => item.Status));
    }

    [Fact]
    public async Task TheCompletedViewHoldsCompletedAndCancelledJobsNewestFirstThenById()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        var technician = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var completed = await DataAt(Now.AddHours(-5)).MoveToAsync(
            await Data.CreateJobAsync(administrator.Id, _templateId, CancellationToken), technician, JobStatus.Completed, CancellationToken);
        var cancelledLatest = await DataAt(Now.AddHours(-1)).MoveToAsync(
            await Data.CreateJobAsync(administrator.Id, _templateId, CancellationToken), technician, JobStatus.Cancelled, CancellationToken);
        var tiedOne = await DataAt(Now.AddHours(-3)).MoveToAsync(
            await Data.CreateJobAsync(administrator.Id, _templateId, CancellationToken), technician, JobStatus.Cancelled, CancellationToken);
        var tiedTwo = await DataAt(Now.AddHours(-3)).MoveToAsync(
            await Data.CreateJobAsync(administrator.Id, _templateId, CancellationToken), technician, JobStatus.Cancelled, CancellationToken);
        await Data.MoveToAsync(await Data.CreateJobAsync(administrator.Id, _templateId, CancellationToken), technician, JobStatus.Assigned, CancellationToken);
        using var client = ClientFor(technician.User);

        using var response = await client.GetAsync(MyJobsPath + "?view=completed");

        var items = await ReadItemsAsync(response);
        var tied = new[] { tiedOne, tiedTwo }.OrderByDescending(job => job.Id.ToString(), StringComparer.Ordinal).Select(job => job.Id);
        Assert.Equal([cancelledLatest.Id, .. tied, completed.Id], items.Select(item => item.Id));
        Assert.Equal(
            [JobStatus.Cancelled, JobStatus.Cancelled, JobStatus.Cancelled, JobStatus.Completed],
            items.Select(item => item.Status));
        var completedItem = items[^1];
        Assert.NotNull(completedItem.StartedAt);
        Assert.NotNull(completedItem.CompletedAt);
        Assert.Null(completedItem.CancelledAt);
        Assert.Equal(Now.AddHours(-1), items[0].CancelledAt);
    }

    [Fact]
    public async Task TheCompletedViewReturnsTheFiftyNewestJobs()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        var technician = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var cancelled = await CreateCancelledJobsAsync(administrator, technician, count: 52);
        using var client = ClientFor(technician.User);

        using var response = await client.GetAsync(MyJobsPath + "?view=completed");

        var items = await ReadItemsAsync(response);
        Assert.Equal(GetAssignedJobsEndpoint.MaxCompletedItems, items.Count);
        Assert.Equal(cancelled.Take(50).Select(job => job.Id), items.Select(item => item.Id));
    }

    [Fact]
    public async Task AnotherTechniciansJobsAndUnassignedJobsNeverAppearInEitherView()
    {
        var world = await ArrangeAsync();
        using var client = ClientFor(world.Technician.User);
        using var otherClient = ClientFor(world.OtherTechnician.User);

        using var active = await client.GetAsync(MyJobsPath);
        using var completed = await client.GetAsync(MyJobsPath + "?view=completed");
        using var otherActive = await otherClient.GetAsync(MyJobsPath);
        using var otherCompleted = await otherClient.GetAsync(MyJobsPath + "?view=completed");

        var mine = (await ReadItemsAsync(active)).Concat(await ReadItemsAsync(completed)).Select(item => item.Id).ToList();
        var theirs = (await ReadItemsAsync(otherActive)).Concat(await ReadItemsAsync(otherCompleted)).Select(item => item.Id).ToList();
        Assert.Equal(world.TechniciansJobs.Select(job => job.Id).Order(), mine.Order());
        Assert.Equal(world.OtherTechniciansJobs.Select(job => job.Id).Order(), theirs.Order());
        Assert.Empty(mine.Intersect(theirs));
        Assert.DoesNotContain(world.Unassigned.Id, mine.Concat(theirs));
    }

    [Fact]
    public async Task AReassignedJobLeavesThePreviousTechniciansList()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        var first = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var second = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var job = await Data.AssignJobAsync(await Data.CreateJobAsync(administrator.Id, _templateId, CancellationToken), first, CancellationToken);
        await DataAt(Now.AddHours(-1)).AssignJobAsync(job, second, CancellationToken);
        using var firstClient = ClientFor(first.User);
        using var secondClient = ClientFor(second.User);

        using var firstList = await firstClient.GetAsync(MyJobsPath);
        using var secondList = await secondClient.GetAsync(MyJobsPath);

        Assert.Empty(await ReadItemsAsync(firstList));
        Assert.Equal([job.Id], (await ReadItemsAsync(secondList)).Select(item => item.Id));
    }

    [Fact]
    public async Task EachItemReadsBackFromAFreshDbContext()
    {
        var world = await ArrangeAsync();
        using var client = ClientFor(world.Technician.User);

        using var response = await client.GetAsync(MyJobsPath);

        var items = await ReadItemsAsync(response);
        await using var db = Fixture.CreateAppDbContext();
        foreach (var item in items)
        {
            var stored = await db.Jobs.AsNoTracking().SingleAsync(job => job.Id == item.Id, CancellationToken);
            Assert.Equal(
                new AssignedJobResponse(
                    stored.Id,
                    stored.Number,
                    stored.Title,
                    stored.Priority,
                    stored.Status,
                    stored.CustomerName,
                    stored.SiteAddress,
                    stored.ScheduledStart,
                    stored.ScheduledEnd,
                    stored.StartedAt,
                    stored.CompletedAt,
                    stored.CancelledAt,
                    stored.UpdatedAt,
                    stored.Version),
                item);
            Assert.Equal(world.Technician.UserId, stored.AssignedTechnicianId);
        }
    }

    [Fact]
    public async Task TheResponseWritesEveryMemberInContractOrderWithInstantsInTheWireFormatAndNullsPresent()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        var technician = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var job = await Data.MoveToAsync(
            await Data.CreateJobAsync(administrator.Id, _templateId, CancellationToken), technician, JobStatus.Assigned, CancellationToken);
        using var client = ClientFor(technician.User);

        using var response = await client.GetAsync(MyJobsPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(["items"], document.RootElement.EnumerateObject().Select(member => member.Name));
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(
            [
                "id", "number", "title", "priority", "status", "customerName", "siteAddress", "scheduledStart",
                "scheduledEnd", "startedAt", "completedAt", "cancelledAt", "updatedAt", "version",
            ],
            item.EnumerateObject().Select(member => member.Name));
        Assert.Equal("Assigned", item.GetProperty("status").GetString());
        Assert.Equal("Normal", item.GetProperty("priority").GetString());
        Assert.Equal(ApiClient.WireInstant(job.ScheduledStart), item.GetProperty("scheduledStart").GetString());
        Assert.Equal(ApiClient.WireInstant(job.UpdatedAt), item.GetProperty("updatedAt").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("startedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("completedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("cancelledAt").ValueKind);
        Assert.Equal(job.Version, item.GetProperty("version").GetUInt32());
    }

    [Fact]
    public async Task ATechnicianWithoutJobsGetsEmptyLists()
    {
        var technician = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        using var client = ClientFor(technician.User);

        using var active = await client.GetAsync(MyJobsPath);
        using var completed = await client.GetAsync(MyJobsPath + "?view=completed");

        Assert.Empty(await ReadItemsAsync(active));
        Assert.Empty(await ReadItemsAsync(completed));
    }

    [Theory]
    [InlineData("view=")]
    [InlineData("view=Active")]
    [InlineData("view=ACTIVE")]
    [InlineData("view=Completed")]
    [InlineData("view=all")]
    [InlineData("view=active%20")]
    [InlineData("view=%20active")]
    [InlineData("view=active%0A")]
    [InlineData("view=active,completed")]
    [InlineData("view=1")]
    [InlineData("view=0")]
    public async Task AnUnknownOrEmptyViewReturns400KeyedViewWithoutEchoingTheValue(string query)
    {
        var technician = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        using var client = ClientFor(technician.User);

        using var response = await client.GetAsync($"{MyJobsPath}?{query}");

        await ApiClient.AssertFieldErrorsAsync(response, "view");
        var text = await response.Content.ReadAsStringAsync(CancellationToken);
        using var problem = JsonDocument.Parse(text);
        Assert.Equal(
            GetAssignedJobsRequest.ViewMessage,
            Assert.Single(problem.RootElement.GetProperty("errors").GetProperty("view").EnumerateArray()).GetString());
        Assert.DoesNotContain("ACTIVE", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"all\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARepeatedViewReturns400KeyedView()
    {
        var technician = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        using var client = ClientFor(technician.User);

        using var response = await client.GetAsync($"{MyJobsPath}?view=active&view=completed");

        await ApiClient.AssertFieldErrorsAsync(response, "view");
    }

    [Fact]
    public async Task UnknownQueryParametersAreIgnored()
    {
        var world = await ArrangeAsync();
        using var client = ClientFor(world.Technician.User);

        using var response = await client.GetAsync($"{MyJobsPath}?technicianId={world.OtherTechnician.UserId}&status=Completed");

        var items = await ReadItemsAsync(response);
        Assert.Equal(4, items.Count);
        Assert.All(items, item => Assert.Contains(item.Id, world.TechniciansJobs.Select(job => job.Id)));
    }

    [Fact]
    public async Task AfterTheDemoSeedAishasActiveJobsAndMarcosCompletedJobsMatchTheSeededData()
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
            var aisha = await db.Users.AsNoTracking().SingleAsync(user => user.Email == "aisha.karim@fieldservice.example", CancellationToken);
            var marco = await db.Users.AsNoTracking().SingleAsync(user => user.Email == "marco.reyes@fieldservice.example", CancellationToken);
            var tom = await db.Users.AsNoTracking().SingleAsync(user => user.Email == "tom.becker@fieldservice.example", CancellationToken);
            using var client = factory.CreateApiClient();

            client.AccessToken = TestTokens.For(factory, aisha);
            using var aishaActive = await client.GetAsync(MyJobsPath);
            using var aishaCompleted = await client.GetAsync(MyJobsPath + "?view=completed");
            client.AccessToken = TestTokens.For(factory, marco);
            using var marcoActive = await client.GetAsync(MyJobsPath);
            using var marcoCompleted = await client.GetAsync(MyJobsPath + "?view=completed");
            client.AccessToken = TestTokens.For(factory, tom);
            using var tomActive = await client.GetAsync(MyJobsPath);

            Assert.Equal([1055L, 1040L, 1041L], (await ReadItemsAsync(aishaActive)).Select(item => item.Number));
            Assert.Equal([1043L], (await ReadItemsAsync(aishaCompleted)).Select(item => item.Number));
            Assert.Equal([1042L, 1047L, 1053L], (await ReadItemsAsync(marcoActive)).Select(item => item.Number));
            var marcoDone = Assert.Single(await ReadItemsAsync(marcoCompleted));
            Assert.Equal(1036, marcoDone.Number);
            Assert.Equal(JobStatus.Cancelled, marcoDone.Status);
            Assert.Equal([1037L, 1038L, 1044L, 1048L], (await ReadItemsAsync(tomActive)).Select(item => item.Number));
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task AnAnonymousCallerGets401()
    {
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.GetAsync(MyJobsPath);

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated");
    }

    [Theory]
    [InlineData("")]
    [InlineData("?view=all")]
    [InlineData("?view=")]
    public async Task AnAdministratorGets403WhateverTheView(string query)
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.GetAsync(MyJobsPath + query);

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.Forbidden, "auth.forbidden");
    }

    private static async Task<IReadOnlyList<AssignedJobResponse>> ReadItemsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<GetAssignedJobsResponse>(ApiClient.Json, TestContext.Current.CancellationToken);
        return body?.Items ?? throw new InvalidOperationException("The response has no jobs body.");
    }

    private TestData DataAt(DateTimeOffset now) => new(Fixture, now);

    private ApiClient ClientFor(User user)
    {
        var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TestTokens.For(Fixture.Factory, user);
        return client;
    }

    private Task<Job> CreateScheduledAsync(User administrator, DateTimeOffset start, long number) =>
        Data.CreateJobAsync(
            administrator.Id,
            TestData.NewDetails(_templateId, Now) with { ScheduledStart = start, ScheduledEnd = start.AddHours(2) },
            CancellationToken,
            number);

    private async Task<World> ArrangeAsync()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        var technician = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var other = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var start = Now.AddDays(1);
        var assignedLatest = await Data.MoveToAsync(
            await CreateScheduledAsync(administrator, start.AddHours(5), 8001), technician, JobStatus.Assigned, CancellationToken);
        var inProgressHighNumber = await Data.MoveToAsync(
            await CreateScheduledAsync(administrator, start.AddHours(2), 8003), technician, JobStatus.InProgress, CancellationToken);
        var assignedLowNumber = await Data.MoveToAsync(
            await CreateScheduledAsync(administrator, start.AddHours(2), 8002), technician, JobStatus.Assigned, CancellationToken);
        var onHoldFirst = await Data.MoveToAsync(
            await CreateScheduledAsync(administrator, start.AddHours(-20), 8004), technician, JobStatus.OnHold, CancellationToken);
        var completed = await Data.MoveToAsync(
            await CreateScheduledAsync(administrator, start.AddHours(-30), 8005), technician, JobStatus.Completed, CancellationToken);
        var cancelled = await Data.MoveToAsync(
            await CreateScheduledAsync(administrator, start.AddHours(-40), 8006), technician, JobStatus.Cancelled, CancellationToken);
        var otherJobs = new List<Job>();
        foreach (var status in new[] { JobStatus.Assigned, JobStatus.InProgress, JobStatus.OnHold, JobStatus.Completed, JobStatus.Cancelled })
        {
            otherJobs.Add(await Data.MoveToAsync(
                await CreateScheduledAsync(administrator, start.AddHours(-50), 8100 + otherJobs.Count), other, status, CancellationToken));
        }

        var unassigned = await CreateScheduledAsync(administrator, start.AddHours(-60), 8200);
        return new World(
            technician,
            other,
            assignedLatest,
            inProgressHighNumber,
            assignedLowNumber,
            onHoldFirst,
            [assignedLatest, inProgressHighNumber, assignedLowNumber, onHoldFirst, completed, cancelled],
            otherJobs,
            unassigned);
    }

    private async Task<List<Job>> CreateCancelledJobsAsync(User administrator, Technician technician, int count)
    {
        var jobs = new List<Job>();
        var cancelledAt = Now.AddMinutes(-1);
        for (var index = 0; index < count; index++)
        {
            var job = TestData.NewJob(administrator.Id, _templateId, Now.AddDays(-1));
            TestData.Require(job.AssignTo(technician, JobActor.Administrator(administrator.Id), Now.AddDays(-1)));
            TestData.Require(job.Cancel(TestData.CancellationReason, JobActor.Administrator(administrator.Id), cancelledAt.AddMinutes(-index)));
            jobs.Add(job);
        }

        await using var db = Fixture.CreateAppDbContext();
        db.Jobs.AddRange(jobs);
        await db.SaveChangesAsync(CancellationToken);
        return jobs;
    }

    private sealed record World(
        Technician Technician,
        Technician OtherTechnician,
        Job AssignedLatest,
        Job InProgressHighNumber,
        Job AssignedLowNumber,
        Job OnHoldFirst,
        IReadOnlyList<Job> TechniciansJobs,
        IReadOnlyList<Job> OtherTechniciansJobs,
        Job Unassigned);
}
