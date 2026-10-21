using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Features.Jobs;
using FieldService.Api.Features.Jobs.GetJobs;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Shared.Http;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Tests.Features.Jobs;

[Collection(DatabaseCollection.Name)]
public sealed class GetJobsTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string JobsPath = "/api/jobs";

    private static readonly Guid _templateId = ChecklistTemplateSeed.TemplateId(2);

    private DateTimeOffset Now => UtcInstant.Truncate(Fixture.Factory.Clock.GetUtcNow());

    [Fact]
    public async Task ListsEveryJobNewestUpdateFirstWithTheAssigneeAndACountForEachOfTheSixStatuses()
    {
        var jobs = await ArrangeAsync();
        using var client = ClientFor(jobs.Administrator);

        using var response = await client.GetAsync(JobsPath);

        var page = await ReadPageAsync(response);
        Assert.Equal(1, page.Page);
        Assert.Equal(GetJobsRequest.DefaultPageSize, page.PageSize);
        Assert.Equal(6, page.TotalCount);
        Assert.Equal([jobs.Completed, jobs.OnHold, jobs.Cancelled, jobs.InProgress, jobs.Assigned, jobs.Unassigned], page.Items.Select(item => item.Id));
        AssertCounts(page, unassigned: 1, assigned: 1, inProgress: 1, onHold: 1, completed: 1, cancelled: 1);

        var assigned = page.Items.Single(item => item.Id == jobs.Assigned);
        Assert.Equal(new JobTechnicianReference(jobs.First.UserId, jobs.First.User.DisplayName, Trade.Hvac), assigned.AssignedTechnician);
        Assert.Equal("Breaker panel check", assigned.Title);
        Assert.Equal(JobPriority.Normal, assigned.Priority);
        Assert.Equal(JobStatus.Assigned, assigned.Status);
        Assert.Equal("Beta Library", assigned.CustomerName);
        Assert.Equal("300 Pine Road", assigned.SiteAddress);
        Assert.Null(page.Items.Single(item => item.Id == jobs.Unassigned).AssignedTechnician);

        await using var db = Fixture.CreateAppDbContext();
        var stored = await db.Jobs.AsNoTracking().ToDictionaryAsync(job => job.Id, CancellationToken);
        Assert.All(page.Items, item =>
        {
            var job = stored[item.Id];
            Assert.Equal(job.Number, item.Number);
            Assert.Equal(job.Version, item.Version);
            Assert.Equal(job.UpdatedAt, item.UpdatedAt);
            Assert.Equal(job.ScheduledStart, item.ScheduledStart);
            Assert.Equal(job.ScheduledEnd, item.ScheduledEnd);
            Assert.Equal(job.AssignedTechnicianId, item.AssignedTechnician?.Id);
        });
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task TheResponseCarriesEveryMemberAndTheStatusCountsInEnumerationOrder()
    {
        var jobs = await ArrangeAsync();
        using var client = ClientFor(jobs.Administrator);

        using var response = await client.GetAsync($"{JobsPath}?technicianId={jobs.Second.UserId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var root = document.RootElement;
        Assert.Equal(["items", "page", "pageSize", "totalCount", "statusCounts"], root.EnumerateObject().Select(member => member.Name));
        Assert.Equal(
            [
                "id", "number", "title", "priority", "status", "customerName", "siteAddress", "scheduledStart",
                "scheduledEnd", "assignedTechnician", "updatedAt", "version",
            ],
            root.GetProperty("items")[0].EnumerateObject().Select(member => member.Name));
        Assert.Equal(
            ["Unassigned", "Assigned", "InProgress", "OnHold", "Completed", "Cancelled"],
            root.GetProperty("statusCounts").EnumerateArray().Select(entry => entry.GetProperty("status").GetString()));
        Assert.Equal(
            [0, 0, 1, 0, 1, 0],
            root.GetProperty("statusCounts").EnumerateArray().Select(entry => entry.GetProperty("count").GetInt32()));
    }

    [Fact]
    public async Task EqualUpdateInstantsAreOrderedByIdDescending()
    {
        var data = new TestData(Fixture, Now);
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        for (var index = 0; index < 4; index++)
        {
            await data.CreateJobAsync(administrator.Id, _templateId, CancellationToken);
        }

        using var client = ClientFor(administrator);

        using var response = await client.GetAsync(JobsPath);

        var ids = (await ReadPageAsync(response)).Items.Select(item => item.Id.ToString()).ToList();
        Assert.Equal(4, ids.Count);
        Assert.Equal(ids.OrderDescending(StringComparer.Ordinal), ids);
    }

    [Fact]
    public async Task FiltersCombineWithAndAndTheStatusCountsIgnoreOnlyTheStatusFilter()
    {
        var jobs = await ArrangeAsync();
        using var client = ClientFor(jobs.Administrator);

        var byStatus = await GetPageAsync(client, "status=Assigned");
        Assert.Equal([jobs.Assigned], byStatus.Items.Select(item => item.Id));
        Assert.Equal(1, byStatus.TotalCount);
        AssertCounts(byStatus, unassigned: 1, assigned: 1, inProgress: 1, onHold: 1, completed: 1, cancelled: 1);

        var byTechnician = await GetPageAsync(client, $"technicianId={jobs.First.UserId}");
        Assert.Equal([jobs.OnHold, jobs.Cancelled, jobs.Assigned], byTechnician.Items.Select(item => item.Id));
        Assert.Equal(3, byTechnician.TotalCount);
        AssertCounts(byTechnician, unassigned: 0, assigned: 1, inProgress: 0, onHold: 1, completed: 0, cancelled: 1);

        var byTechnicianAndStatus = await GetPageAsync(client, $"technicianId={jobs.First.UserId}&status=OnHold");
        Assert.Equal([jobs.OnHold], byTechnicianAndStatus.Items.Select(item => item.Id));
        Assert.Equal(1, byTechnicianAndStatus.TotalCount);
        AssertCounts(byTechnicianAndStatus, unassigned: 0, assigned: 1, inProgress: 0, onHold: 1, completed: 0, cancelled: 1);

        var byPriority = await GetPageAsync(client, "priority=High");
        Assert.Equal([jobs.OnHold, jobs.Unassigned], byPriority.Items.Select(item => item.Id));
        AssertCounts(byPriority, unassigned: 1, assigned: 0, inProgress: 0, onHold: 1, completed: 0, cancelled: 0);

        var byPriorityAndTechnician = await GetPageAsync(client, $"priority=High&technicianId={jobs.First.UserId}");
        Assert.Equal([jobs.OnHold], byPriorityAndTechnician.Items.Select(item => item.Id));

        var noMatch = await GetPageAsync(client, "status=Completed&priority=Low");
        Assert.Empty(noMatch.Items);
        Assert.Equal(0, noMatch.TotalCount);
        AssertCounts(noMatch, unassigned: 0, assigned: 0, inProgress: 0, onHold: 0, completed: 0, cancelled: 1);
    }

    [Fact]
    public async Task ATechnicianWithNoJobsGivesAnEmptyPageWithZeroCounts()
    {
        var jobs = await ArrangeAsync();
        using var client = ClientFor(jobs.Administrator);

        var page = await GetPageAsync(client, $"technicianId={Guid.CreateVersion7()}");

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        AssertCounts(page, unassigned: 0, assigned: 0, inProgress: 0, onHold: 0, completed: 0, cancelled: 0);
    }

    [Fact]
    public async Task SearchMatchesTitleCustomerAndSiteWithoutRegardToCase()
    {
        var jobs = await ArrangeAsync();
        using var client = ClientFor(jobs.Administrator);

        var byCustomer = await GetPageAsync(client, "search=aLpHa");
        Assert.Equal([jobs.Cancelled, jobs.Unassigned], byCustomer.Items.Select(item => item.Id));
        AssertCounts(byCustomer, unassigned: 1, assigned: 0, inProgress: 0, onHold: 0, completed: 0, cancelled: 1);

        var bySite = await GetPageAsync(client, "search=ROAD");
        Assert.Equal([jobs.Assigned, jobs.Unassigned], bySite.Items.Select(item => item.Id));

        var byTitle = await GetPageAsync(client, "search=panel");
        Assert.Equal([jobs.OnHold, jobs.Assigned], byTitle.Items.Select(item => item.Id));

        var trimmed = await GetPageAsync(client, $"search={Uri.EscapeDataString("   alpha  ")}");
        Assert.Equal([jobs.Cancelled, jobs.Unassigned], trimmed.Items.Select(item => item.Id));

        var withStatus = await GetPageAsync(client, "search=alpha&status=Cancelled");
        Assert.Equal([jobs.Cancelled], withStatus.Items.Select(item => item.Id));
        AssertCounts(withStatus, unassigned: 1, assigned: 0, inProgress: 0, onHold: 0, completed: 0, cancelled: 1);
    }

    [Fact]
    public async Task SearchFindsAJobByItsNumberWithOrWithoutTheJobPrefix()
    {
        var jobs = await ArrangeAsync();
        using var client = ClientFor(jobs.Administrator);
        await using var db = Fixture.CreateAppDbContext();
        var number = (await db.Jobs.AsNoTracking().SingleAsync(job => job.Id == jobs.InProgress, CancellationToken)).Number;

        foreach (var search in new[] { $"{number}", $"job-{number}", $"JOB-{number}", $" Job-{number} " })
        {
            var page = await GetPageAsync(client, $"search={Uri.EscapeDataString(search)}");

            Assert.Equal([jobs.InProgress], page.Items.Select(item => item.Id));
        }
    }

    [Fact]
    public async Task PercentUnderscoreAndBackslashInASearchMatchLiterally()
    {
        var data = new TestData(Fixture, Now);
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var percent = await CreateTitledAsync(data, administrator, "Save 50% on filters");
        await CreateTitledAsync(data, administrator, "Save 500 on filters");
        var underscore = await CreateTitledAsync(data, administrator, "Unit a_b relay");
        await CreateTitledAsync(data, administrator, "Unit axb relay");
        var backslash = await CreateTitledAsync(data, administrator, "Share \\\\srv\\plans");
        await CreateTitledAsync(data, administrator, "Share srvplans");
        using var client = ClientFor(administrator);

        Assert.Equal([percent], (await GetPageAsync(client, $"search={Uri.EscapeDataString("50%")}")).Items.Select(item => item.Id));
        Assert.Equal([underscore], (await GetPageAsync(client, $"search={Uri.EscapeDataString("a_b")}")).Items.Select(item => item.Id));
        Assert.Equal([backslash], (await GetPageAsync(client, $"search={Uri.EscapeDataString("v\\p")}")).Items.Select(item => item.Id));
        Assert.Equal([percent], (await GetPageAsync(client, $"search={Uri.EscapeDataString("%")}")).Items.Select(item => item.Id));
        Assert.Equal([underscore], (await GetPageAsync(client, "search=_")).Items.Select(item => item.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("%20%20%20")]
    public async Task AnEmptyOrWhiteSpaceSearchDoesNotFilter(string search)
    {
        var jobs = await ArrangeAsync();
        using var client = ClientFor(jobs.Administrator);

        var page = await GetPageAsync(client, $"search={search}");

        Assert.Equal(6, page.TotalCount);
    }

    [Fact]
    public async Task ASearchOfOneHundredCharactersAfterTrimmingIsAcceptedAndOneHundredAndOneIsRejected()
    {
        var administrator = await new TestData(Fixture, Now).CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);
        var accepted = $"  {new string('q', 100)}  ";
        var rejected = new string('q', 101);

        using var ok = await client.GetAsync($"{JobsPath}?search={Uri.EscapeDataString(accepted)}");
        using var tooLong = await client.GetAsync($"{JobsPath}?search={rejected}");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        await ApiClient.AssertFieldErrorsAsync(tooLong, "search");
        Assert.DoesNotContain(rejected, await tooLong.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a%09b")]
    [InlineData("a%00b")]
    [InlineData("line%0Abreak")]
    [InlineData("a%7Fb")]
    public async Task ASearchWithAControlCharacterIsAFieldErrorKeyedSearch(string search)
    {
        var administrator = await new TestData(Fixture, Now).CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.GetAsync($"{JobsPath}?search={search}");

        await ApiClient.AssertFieldErrorsAsync(response, "search");
    }

    [Fact]
    public async Task PagesFollowTheOrderAndAPagePastTheEndIsEmptyWithTheRealTotal()
    {
        var jobs = await ArrangeAsync();
        using var client = ClientFor(jobs.Administrator);

        var second = await GetPageAsync(client, "page=2&pageSize=2");
        var third = await GetPageAsync(client, "page=3&pageSize=2");
        var partial = await GetPageAsync(client, "page=2&pageSize=4");
        var pastTheEnd = await GetPageAsync(client, "page=4&pageSize=2");
        var largest = await GetPageAsync(client, "page=10000&pageSize=100");

        Assert.Equal([jobs.Cancelled, jobs.InProgress], second.Items.Select(item => item.Id));
        Assert.Equal([jobs.Assigned, jobs.Unassigned], third.Items.Select(item => item.Id));
        Assert.Equal([jobs.Assigned, jobs.Unassigned], partial.Items.Select(item => item.Id));
        Assert.Equal(2, second.Page);
        Assert.Equal(2, second.PageSize);
        Assert.Equal(6, second.TotalCount);
        Assert.Empty(pastTheEnd.Items);
        Assert.Equal(6, pastTheEnd.TotalCount);
        Assert.Equal(4, pastTheEnd.Page);
        Assert.Empty(largest.Items);
        Assert.Equal(10000, largest.Page);
        Assert.Equal(100, largest.PageSize);
        Assert.Equal(6, largest.TotalCount);
    }

    [Theory]
    [InlineData("page=0", "page")]
    [InlineData("page=-1", "page")]
    [InlineData("page=10001", "page")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("page=0&pageSize=101", "page", "pageSize")]
    public async Task PagingOutsideItsRangeIsAFieldErrorKeyedByTheParameter(string query, params string[] keys)
    {
        var administrator = await new TestData(Fixture, Now).CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.GetAsync($"{JobsPath}?{query}");

        await ApiClient.AssertFieldErrorsAsync(response, keys);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var errors = problem.RootElement.GetProperty("errors");
        if (errors.TryGetProperty("page", out var page))
        {
            Assert.Equal("Enter a page from 1 to 10000.", page[0].GetString());
        }

        if (errors.TryGetProperty("pageSize", out var pageSize))
        {
            Assert.Equal("Enter a page size from 1 to 100.", pageSize[0].GetString());
        }
    }

    [Theory]
    [InlineData("status=Bogus", "status")]
    [InlineData("status=1", "status")]
    [InlineData("status=99", "status")]
    [InlineData("status=-1", "status")]
    [InlineData("status=inprogress", "status")]
    [InlineData("status=Assigned,InProgress", "status")]
    [InlineData("status=%20Assigned", "status")]
    [InlineData("status=Assigned&status=OnHold", "status")]
    [InlineData("priority=2", "priority")]
    [InlineData("priority=Critical", "priority")]
    [InlineData("technicianId=abc", "technicianId")]
    [InlineData("technicianId=1042", "technicianId")]
    [InlineData("page=abc", "page")]
    [InlineData("page=1.5", "page")]
    [InlineData("pageSize=ten", "pageSize")]
    [InlineData("pageSize=99999999999", "pageSize")]
    [InlineData("search=a&search=b", "search")]
    [InlineData("status=1&technicianId=abc&page=x", "status", "technicianId", "page")]
    public async Task ABadQueryValueIsAFieldErrorKeyedByTheParameterName(string query, params string[] keys)
    {
        var administrator = await new TestData(Fixture, Now).CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.GetAsync($"{JobsPath}?{query}");

        await ApiClient.AssertFieldErrorsAsync(response, keys);
    }

    [Fact]
    public async Task AQueryErrorNamesTheAllowedValuesWithoutEchoingTheValueSent()
    {
        var administrator = await new TestData(Fixture, Now).CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);
        var marker = $"marker{Guid.NewGuid():N}";

        using var response = await client.GetAsync($"{JobsPath}?status={marker}&technicianId={marker}");

        var text = await response.Content.ReadAsStringAsync(CancellationToken);
        await ApiClient.AssertFieldErrorsAsync(response, "status", "technicianId");
        Assert.DoesNotContain(marker, text, StringComparison.Ordinal);
        using var problem = JsonDocument.Parse(text);
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(
            "The value must be one of Unassigned, Assigned, InProgress, OnHold, Completed or Cancelled.",
            errors.GetProperty("status")[0].GetString());
        Assert.Equal("The value must be a UUID.", errors.GetProperty("technicianId")[0].GetString());
        Assert.Equal("/api/jobs", problem.RootElement.GetProperty("instance").GetString());
    }

    [Fact]
    public async Task OmittedParametersTakeTheirDefaults()
    {
        var jobs = await ArrangeAsync();
        using var client = ClientFor(jobs.Administrator);

        var page = await GetPageAsync(client, "unknown=ignored");

        Assert.Equal(6, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(GetJobsRequest.DefaultPageSize, page.PageSize);
    }

    [Fact]
    public async Task AnEmptyValueForAParameterThatIsNotTextIsAFieldErrorKeyedByItsName()
    {
        var administrator = await new TestData(Fixture, Now).CreateAdministratorAsync(CancellationToken);
        using var client = ClientFor(administrator);

        using var response = await client.GetAsync($"{JobsPath}?search=&status=&priority=&technicianId=&page=&pageSize=");

        await ApiClient.AssertFieldErrorsAsync(response, "status", "priority", "technicianId", "page", "pageSize");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(
            "Enter a value, or leave the parameter out.",
            problem.RootElement.GetProperty("errors").GetProperty("page")[0].GetString());
    }

    [Fact]
    public async Task AnAnonymousCallerGets401()
    {
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.GetAsync(JobsPath);

        await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated");
    }

    [Fact]
    public async Task ATechnicianGets403EvenWithABadQueryValue()
    {
        var technician = await new TestData(Fixture, Now).CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        using var client = ClientFor(technician.User);

        using var plain = await client.GetAsync(JobsPath);
        using var withBadValue = await client.GetAsync($"{JobsPath}?status=1");

        await ApiClient.AssertProblemAsync(plain, HttpStatusCode.Forbidden, "auth.forbidden");
        await ApiClient.AssertProblemAsync(withBadValue, HttpStatusCode.Forbidden, "auth.forbidden");
    }

    [Fact]
    public async Task ARateLimitRejectionLogsTheJobsRouteWithoutATrailingSlash()
    {
        using var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = "not-a-token";

        for (var attempt = 1; attempt <= RateLimitPolicies.AnonymousLimit; attempt++)
        {
            using var allowed = await client.GetAsync(JobsPath);
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        using var limited = await client.GetAsync(JobsPath);

        await ApiClient.AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, "rate-limit.exceeded");
        var rejected = Assert.Single(Fixture.Factory.LogSink.Named(SecurityLog.RateLimitRejectedEvent));
        Assert.Equal("global", CapturingLogSink.Scalar(rejected, "Policy"));
        Assert.Equal("/api/jobs", CapturingLogSink.Scalar(rejected, "RoutePattern"));
    }

    private static void AssertCounts(
        GetJobsResponse page,
        int unassigned,
        int assigned,
        int inProgress,
        int onHold,
        int completed,
        int cancelled)
    {
        Assert.Equal(
            [
                new JobStatusCount(JobStatus.Unassigned, unassigned),
                new JobStatusCount(JobStatus.Assigned, assigned),
                new JobStatusCount(JobStatus.InProgress, inProgress),
                new JobStatusCount(JobStatus.OnHold, onHold),
                new JobStatusCount(JobStatus.Completed, completed),
                new JobStatusCount(JobStatus.Cancelled, cancelled),
            ],
            page.StatusCounts);
    }

    private static async Task<GetJobsResponse> ReadPageAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var page = await response.Content.ReadFromJsonAsync<GetJobsResponse>(ApiClient.Json, TestContext.Current.CancellationToken);
        return page ?? throw new InvalidOperationException("The response has no page body.");
    }

    private static async Task<GetJobsResponse> GetPageAsync(ApiClient client, string query)
    {
        using var response = await client.GetAsync($"{JobsPath}?{query}");
        return await ReadPageAsync(response);
    }

    private async Task<Guid> CreateTitledAsync(TestData data, User administrator, string title) =>
        (await data.CreateJobAsync(administrator.Id, TestData.NewDetails(_templateId, data.Now, title: title), CancellationToken)).Id;

    private ApiClient ClientFor(User user)
    {
        var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = TestTokens.For(Fixture.Factory, user);
        return client;
    }

    private async Task<Arrangement> ArrangeAsync()
    {
        var start = Now.AddHours(-1);
        TestData At(int minutes) => new(Fixture, start.AddMinutes(minutes));

        var administrator = await At(0).CreateAdministratorAsync(CancellationToken);
        var first = await At(0).CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var second = await At(0).CreateTechnicianAsync(Trade.Electrical, CancellationToken);

        var unassigned = await At(0).CreateJobAsync(
            administrator.Id,
            TestData.NewDetails(_templateId, start, "Rooftop unit service", "Alpha Clinic", "10 Harbor Road", JobPriority.High),
            CancellationToken);

        var assigned = await At(1).CreateJobAsync(
            administrator.Id,
            TestData.NewDetails(_templateId, start, "Breaker panel check", "Beta Library", "300 Pine Road"),
            CancellationToken);
        await At(2).AssignJobAsync(assigned, first, CancellationToken);

        var inProgress = await At(3).CreateJobAsync(
            administrator.Id,
            TestData.NewDetails(_templateId, start, "Drain repair", "Gamma Apartments", "600 Bay Avenue", JobPriority.Urgent),
            CancellationToken);
        await At(3).AssignJobAsync(inProgress, second, CancellationToken);
        await At(4).ChangeStatusAsync(inProgress, JobStatus.InProgress, CancellationToken);

        var cancelled = await At(5).CreateJobAsync(
            administrator.Id,
            TestData.NewDetails(_templateId, start, "Exit sign test", "Alpha Warehouse", "42 Dock Lane", JobPriority.Low),
            CancellationToken);
        await At(5).AssignJobAsync(cancelled, first, CancellationToken);
        await At(6).CancelJobAsync(cancelled, CancellationToken);

        var onHold = await At(7).CreateJobAsync(
            administrator.Id,
            TestData.NewDetails(_templateId, start, "Alarm panel trouble", "Delta School", "18 Ridge Drive", JobPriority.High),
            CancellationToken);
        await At(7).AssignJobAsync(onHold, first, CancellationToken);
        await At(7).ChangeStatusAsync(onHold, JobStatus.InProgress, CancellationToken);
        await At(8).ChangeStatusAsync(onHold, JobStatus.OnHold, CancellationToken, TestData.HoldNote);

        var completed = await At(9).CreateJobAsync(
            administrator.Id,
            TestData.NewDetails(_templateId, start, "Coil cleaning", "Epsilon Offices", "7 Market Street"),
            CancellationToken);
        await At(30).CreateInspectionAsync(completed, second, submitted: true, CancellationToken);

        return new Arrangement(
            administrator,
            first,
            second,
            unassigned.Id,
            assigned.Id,
            inProgress.Id,
            onHold.Id,
            completed.Id,
            cancelled.Id);
    }

    private sealed record Arrangement(
        User Administrator,
        Technician First,
        Technician Second,
        Guid Unassigned,
        Guid Assigned,
        Guid InProgress,
        Guid OnHold,
        Guid Completed,
        Guid Cancelled);
}
