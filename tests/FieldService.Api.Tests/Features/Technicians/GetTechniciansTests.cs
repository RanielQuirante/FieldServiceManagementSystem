using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Features.Technicians.GetTechnicians;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Tests.Features.Technicians;

[Collection(DatabaseCollection.Name)]
public sealed class GetTechniciansTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string TechniciansPath = "/api/technicians";

    private static readonly Guid _templateId = ChecklistTemplateSeed.TemplateId(1);

    private DateTimeOffset Now => UtcInstant.Truncate(Fixture.Factory.Clock.GetUtcNow());

    private TestData Data => new(Fixture, Now.AddHours(-6));

    [Fact]
    public async Task TechniciansAreOrderedByDisplayNameThenIdWithTradeShiftEmailAndPhone()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        var bravo = await Data.CreateTechnicianAsync(Trade.Plumbing, CancellationToken, displayName: "Bravo Tech");
        var charlieOne = await Data.CreateTechnicianAsync(Trade.FireSafety, CancellationToken, displayName: "Charlie Tech");
        var alpha = await Data.CreateTechnicianAsync(Trade.Hvac, CancellationToken, isOnShift: false, displayName: "Alpha Tech");
        var charlieTwo = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken, displayName: "Charlie Tech");
        using var client = ClientFor(administrator);

        using var response = await client.GetAsync(TechniciansPath);

        var items = await ReadItemsAsync(response);
        var charlies = new[] { charlieOne, charlieTwo }.OrderBy(technician => technician.UserId.ToString(), StringComparer.Ordinal);
        Assert.Equal(
            [alpha.UserId, bravo.UserId, .. charlies.Select(technician => technician.UserId)],
            items.Select(item => item.Id));
        foreach (var technician in new[] { alpha, bravo, charlieOne, charlieTwo })
        {
            var item = Assert.Single(items, item => item.Id == technician.UserId);
            Assert.Equal(technician.User.DisplayName, item.DisplayName);
            Assert.Equal(technician.User.Email, item.Email);
            Assert.Equal(technician.User.Email.ToLowerInvariant(), item.Email);
            Assert.Equal(technician.PhoneNumber, item.PhoneNumber);
            Assert.Equal(technician.Trade, item.Trade);
            Assert.Equal(technician.IsOnShift, item.IsOnShift);
        }
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public async Task AvailableForAssignmentHoldsOnlyForAnActiveTechnicianOnShift(bool isOnShift, bool isActive, bool available)
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        var technician = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken, isOnShift, isActive);
        using var client = ClientFor(administrator);

        using var response = await client.GetAsync(TechniciansPath);

        var item = Assert.Single(await ReadItemsAsync(response));
        Assert.Equal(technician.UserId, item.Id);
        Assert.Equal(isActive, item.IsActive);
        Assert.Equal(isOnShift, item.IsOnShift);
        Assert.Equal(available, item.AvailableForAssignment);
    }

    [Fact]
    public async Task OpenJobCountCountsTheTechniciansAssignedInProgressAndOnHoldJobsOnly()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        var technician = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var other = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        foreach (var status in new[] { JobStatus.Assigned, JobStatus.Assigned, JobStatus.InProgress, JobStatus.OnHold, JobStatus.Completed, JobStatus.Cancelled })
        {
            await Data.MoveToAsync(await Data.CreateJobAsync(administrator.Id, _templateId, CancellationToken), technician, status, CancellationToken);
        }

        await Data.MoveToAsync(await Data.CreateJobAsync(administrator.Id, _templateId, CancellationToken), other, JobStatus.Assigned, CancellationToken);
        await Data.CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.GetAsync(TechniciansPath);

        var items = await ReadItemsAsync(response);
        Assert.Equal(4, items.Single(item => item.Id == technician.UserId).OpenJobCount);
        Assert.Equal(1, items.Single(item => item.Id == other.UserId).OpenJobCount);
        await using var db = Fixture.CreateAppDbContext();
        var storedOpen = await db.Jobs.AsNoTracking()
            .CountAsync(
                job => job.AssignedTechnicianId == technician.UserId
                    && job.Status != JobStatus.Completed
                    && job.Status != JobStatus.Cancelled,
                CancellationToken);
        Assert.Equal(storedOpen, items.Single(item => item.Id == technician.UserId).OpenJobCount);
    }

    [Fact]
    public async Task TheCurrentJobIsTheInProgressJobEvenWhenAnOnHoldJobWasUpdatedLater()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        var technician = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var inProgress = await DataAt(Now.AddHours(-3)).MoveToAsync(
            await Data.CreateJobAsync(administrator.Id, _templateId, CancellationToken), technician, JobStatus.InProgress, CancellationToken);
        await DataAt(Now.AddHours(-1)).MoveToAsync(
            await Data.CreateJobAsync(administrator.Id, _templateId, CancellationToken), technician, JobStatus.OnHold, CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.GetAsync(TechniciansPath);

        var item = Assert.Single(await ReadItemsAsync(response));
        Assert.Equal(
            new TechnicianJobReference(inProgress.Id, inProgress.Number, JobStatus.InProgress, inProgress.ScheduledStart, inProgress.ScheduledEnd),
            item.CurrentJob);
    }

    [Fact]
    public async Task WithoutAnInProgressJobTheCurrentJobIsTheOnHoldJobWithTheLatestUpdate()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        var technician = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        await DataAt(Now.AddHours(-3)).MoveToAsync(
            await Data.CreateJobAsync(administrator.Id, _templateId, CancellationToken), technician, JobStatus.OnHold, CancellationToken);
        var latest = await DataAt(Now.AddHours(-1)).MoveToAsync(
            await Data.CreateJobAsync(administrator.Id, _templateId, CancellationToken), technician, JobStatus.OnHold, CancellationToken);
        await DataAt(Now.AddHours(-2)).MoveToAsync(
            await Data.CreateJobAsync(administrator.Id, _templateId, CancellationToken), technician, JobStatus.OnHold, CancellationToken);
        await DataAt(Now.AddMinutes(-30)).MoveToAsync(
            await Data.CreateJobAsync(administrator.Id, _templateId, CancellationToken), technician, JobStatus.Completed, CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.GetAsync(TechniciansPath);

        var item = Assert.Single(await ReadItemsAsync(response));
        Assert.Equal(latest.Id, item.CurrentJob?.Id);
        Assert.Equal(JobStatus.OnHold, item.CurrentJob?.Status);
    }

    [Fact]
    public async Task TheNextJobIsTheAssignedJobWithTheEarliestScheduledStartThenTheLowestNumber()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        var technician = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var start = Now.AddDays(1);
        var later = await CreateScheduledAsync(administrator, start.AddHours(-1), number: 7001);
        var higher = await CreateScheduledAsync(administrator, start.AddHours(-2), number: 7003);
        var lower = await CreateScheduledAsync(administrator, start.AddHours(-2), number: 7002);
        var unassigned = await CreateScheduledAsync(administrator, start.AddHours(-5), number: 7004);
        var inProgress = await CreateScheduledAsync(administrator, start.AddHours(-4), number: 7005);
        foreach (var job in new[] { later, higher, lower })
        {
            await Data.MoveToAsync(job, technician, JobStatus.Assigned, CancellationToken);
        }

        await Data.MoveToAsync(inProgress, technician, JobStatus.InProgress, CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.GetAsync(TechniciansPath);

        var item = Assert.Single(await ReadItemsAsync(response));
        Assert.Equal(
            new TechnicianJobReference(lower.Id, 7002, JobStatus.Assigned, lower.ScheduledStart, lower.ScheduledEnd),
            item.NextJob);
        Assert.Equal(inProgress.Id, item.CurrentJob?.Id);
        Assert.Equal(4, item.OpenJobCount);
        Assert.NotEqual(unassigned.Id, item.NextJob?.Id);
    }

    [Fact]
    public async Task ATechnicianWithoutOpenJobsHasACountOfZeroAndNoCurrentOrNextJob()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        var technician = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        await Data.MoveToAsync(await Data.CreateJobAsync(administrator.Id, _templateId, CancellationToken), technician, JobStatus.Cancelled, CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.GetAsync(TechniciansPath);

        var item = Assert.Single(await ReadItemsAsync(response));
        Assert.Equal(0, item.OpenJobCount);
        Assert.Null(item.CurrentJob);
        Assert.Null(item.NextJob);
    }

    [Fact]
    public async Task TheResponseWritesEveryMemberInContractOrderWithInstantsInTheWireFormatAndNullsPresent()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        var technician = await Data.CreateTechnicianAsync(Trade.FireSafety, CancellationToken);
        var job = await Data.MoveToAsync(
            await Data.CreateJobAsync(administrator.Id, _templateId, CancellationToken), technician, JobStatus.Assigned, CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.GetAsync(TechniciansPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(["items"], document.RootElement.EnumerateObject().Select(member => member.Name));
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(
            [
                "id", "displayName", "email", "phoneNumber", "trade", "isActive", "isOnShift",
                "availableForAssignment", "openJobCount", "currentJob", "nextJob",
            ],
            item.EnumerateObject().Select(member => member.Name));
        Assert.Equal("FireSafety", item.GetProperty("trade").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("currentJob").ValueKind);
        var next = item.GetProperty("nextJob");
        Assert.Equal(["id", "number", "status", "scheduledStart", "scheduledEnd"], next.EnumerateObject().Select(member => member.Name));
        Assert.Equal(job.Number, next.GetProperty("number").GetInt64());
        Assert.Equal("Assigned", next.GetProperty("status").GetString());
        Assert.Equal(ApiClient.WireInstant(job.ScheduledStart), next.GetProperty("scheduledStart").GetString());
        Assert.Equal(ApiClient.WireInstant(job.ScheduledEnd), next.GetProperty("scheduledEnd").GetString());
    }

    [Fact]
    public async Task AnEmptyTeamGivesAnEmptyList()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.GetAsync(TechniciansPath);

        Assert.Empty(await ReadItemsAsync(response));
    }

    [Fact]
    public async Task ReadingTechniciansWritesNothingAndRecordsNoEvent()
    {
        var administrator = await Data.CreateAdministratorAsync(CancellationToken);
        var technician = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        var job = await Data.MoveToAsync(
            await Data.CreateJobAsync(administrator.Id, _templateId, CancellationToken), technician, JobStatus.Assigned, CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.GetAsync(TechniciansPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = Fixture.CreateAppDbContext();
        Assert.Equal(job.Version, (await db.Jobs.AsNoTracking().SingleAsync(entry => entry.Id == job.Id, CancellationToken)).Version);
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task AfterTheDemoSeedTheSixTechniciansCarryTheWorkloadOfTheSeededJobs()
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
            using var client = factory.CreateApiClient();
            client.AccessToken = TestTokens.For(factory, dana);

            using var response = await client.GetAsync(TechniciansPath);

            var items = await ReadItemsAsync(response);
            Assert.Equal(
                ["Aisha Karim", "Lena Park", "Marco Reyes", "Priya Natarajan", "Samuel Osei", "Tom Becker"],
                items.Select(item => item.DisplayName));
            Assert.Equal(
                [Trade.Electrical, Trade.FireSafety, Trade.Hvac, Trade.Electrical, Trade.Hvac, Trade.Plumbing],
                items.Select(item => item.Trade));
            Assert.Equal([3, 1, 3, 2, 0, 4], items.Select(item => item.OpenJobCount));
            Assert.Equal([null, null, 1042L, 1049L, null, 1044L], items.Select(item => item.CurrentJob?.Number));
            Assert.Equal([1055L, 1045L, 1047L, 1046L, null, 1048L], items.Select(item => item.NextJob?.Number));
            Assert.Equal([true, true, true, true, false, true], items.Select(item => item.IsOnShift));
            Assert.Equal([true, true, true, true, false, true], items.Select(item => item.AvailableForAssignment));
            Assert.All(items, item => Assert.True(item.IsActive));
            Assert.Equal("aisha.karim@fieldservice.example", items[0].Email);
            Assert.Equal("(555) 014-7810", items[0].PhoneNumber);
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

        using var response = await client.GetAsync(TechniciansPath);

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated");
    }

    [Fact]
    public async Task ATechnicianGets403()
    {
        var technician = await Data.CreateTechnicianAsync(Trade.Electrical, CancellationToken);
        using var client = ClientFor(technician.User);

        using var response = await client.GetAsync(TechniciansPath);

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.Forbidden, "auth.forbidden");
    }

    private static async Task<IReadOnlyList<TechnicianResponse>> ReadItemsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<GetTechniciansResponse>(ApiClient.Json, TestContext.Current.CancellationToken);
        return body?.Items ?? throw new InvalidOperationException("The response has no technicians body.");
    }

    private TestData DataAt(DateTimeOffset now) => new(Fixture, now);

    private Task<Job> CreateScheduledAsync(User administrator, DateTimeOffset start, long number) =>
        Data.CreateJobAsync(
            administrator.Id,
            TestData.NewDetails(_templateId, Now) with { ScheduledStart = start, ScheduledEnd = start.AddHours(2) },
            CancellationToken,
            number);

    private ApiClient ClientFor(User user)
    {
        var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TestTokens.For(Fixture.Factory, user);
        return client;
    }
}
