using System.Net;
using System.Text;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Tests.Security;

[Collection(DatabaseCollection.Name)]
public sealed class RoleMatrixTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    public const string Anonymous = "Anonymous";

    public const string AssignedTechnician = "AssignedTechnician";

    public const string OtherTechnician = "OtherTechnician";

    public const string Administrator = "Administrator";

    private const string JobRoute = "/api/jobs/{jobId:guid}";

    private const string AssignmentRoute = "/api/jobs/{jobId:guid}/assignment";

    private const string StatusRoute = "/api/jobs/{jobId:guid}/status";

    private const string SubmissionRoute = "/api/jobs/{jobId:guid}/submission";

    private const string InspectionRoute = "/api/jobs/{jobId:guid}/inspection";

    private const string TechniciansRoute = "/api/technicians";

    private const string MyJobsRoute = "/api/technicians/me/jobs";

    private const string DocumentRoute = "/openapi/{documentName}.json";

    private const string SocketTicketsRoute = "/api/auth/socket-tickets";

    private const string DashboardRoute = "/api/dashboard/summary";

    private const string CancellationRoute = "/api/jobs/{jobId:guid}/cancellation";

    private const string ClientErrorsRoute = "/api/client-errors";

    private static readonly string[] _callers = [Anonymous, AssignedTechnician, OtherTechnician, Administrator];

    private static readonly Dictionary<string, string[]> _invalidAdministratorRequests = new(StringComparer.Ordinal)
    {
        ["GetDashboardSummary without from and to"] = ["from", "to"],
        ["GetDashboardSummary with to before from"] = ["to"],
        ["CancelJob with an empty object"] = ["reason", "version"],
        ["CancelJob with a body that is not JSON"] = ["body"],
        ["CancelJob with a reason of 501 characters"] = ["reason"],
        ["CancelJob without a body"] = ["body"],
        ["ReportClientError with an empty object"] = ["message", "occurredAt", "source"],
        ["ReportClientError with a route holding a query string"] = ["route"],
        ["ReportClientError with a body that is not JSON"] = ["body"],
    };

    public static TheoryData<string, string, int, string> InvalidAdministratorRequests
    {
        get
        {
            var data = new TheoryData<string, string, int, string>();
            foreach (var request in _invalidAdministratorRequests.Keys)
            {
                data.Add(request, Anonymous, 401, "auth.unauthenticated");
                data.Add(request, AssignedTechnician, 403, "auth.forbidden");
                data.Add(request, OtherTechnician, 403, "auth.forbidden");
                data.Add(request, Administrator, 400, "request.validation-failed");
            }

            return data;
        }
    }

    public static TheoryData<string, string, string, int, string?> Matrix => new()
    {
        { "POST", "/api/auth/sign-in", Anonymous, 401, "auth.invalid-credentials" },
        { "POST", "/api/auth/sign-in", AssignedTechnician, 401, "auth.invalid-credentials" },
        { "POST", "/api/auth/sign-in", OtherTechnician, 401, "auth.invalid-credentials" },
        { "POST", "/api/auth/sign-in", Administrator, 401, "auth.invalid-credentials" },
        { "POST", "/api/auth/refresh", Anonymous, 401, "auth.session-expired" },
        { "POST", "/api/auth/refresh", AssignedTechnician, 401, "auth.session-expired" },
        { "POST", "/api/auth/refresh", OtherTechnician, 401, "auth.session-expired" },
        { "POST", "/api/auth/refresh", Administrator, 401, "auth.session-expired" },
        { "POST", "/api/auth/sign-out", Anonymous, 204, null },
        { "POST", "/api/auth/sign-out", AssignedTechnician, 204, null },
        { "POST", "/api/auth/sign-out", OtherTechnician, 204, null },
        { "POST", "/api/auth/sign-out", Administrator, 204, null },
        { "POST", SocketTicketsRoute, Anonymous, 401, "auth.unauthenticated" },
        { "POST", SocketTicketsRoute, AssignedTechnician, 403, "auth.forbidden" },
        { "POST", SocketTicketsRoute, OtherTechnician, 403, "auth.forbidden" },
        { "POST", SocketTicketsRoute, Administrator, 200, null },
        { "GET", DashboardRoute, Anonymous, 401, "auth.unauthenticated" },
        { "GET", DashboardRoute, AssignedTechnician, 403, "auth.forbidden" },
        { "GET", DashboardRoute, OtherTechnician, 403, "auth.forbidden" },
        { "GET", DashboardRoute, Administrator, 200, null },
        { "GET", "/api/checklist-templates", Anonymous, 401, "auth.unauthenticated" },
        { "GET", "/api/checklist-templates", AssignedTechnician, 403, "auth.forbidden" },
        { "GET", "/api/checklist-templates", OtherTechnician, 403, "auth.forbidden" },
        { "GET", "/api/checklist-templates", Administrator, 200, null },
        { "GET", TechniciansRoute, Anonymous, 401, "auth.unauthenticated" },
        { "GET", TechniciansRoute, AssignedTechnician, 403, "auth.forbidden" },
        { "GET", TechniciansRoute, OtherTechnician, 403, "auth.forbidden" },
        { "GET", TechniciansRoute, Administrator, 200, null },
        { "GET", MyJobsRoute, Anonymous, 401, "auth.unauthenticated" },
        { "GET", MyJobsRoute, AssignedTechnician, 200, null },
        { "GET", MyJobsRoute, OtherTechnician, 200, null },
        { "GET", MyJobsRoute, Administrator, 403, "auth.forbidden" },
        { "POST", "/api/jobs", Anonymous, 401, "auth.unauthenticated" },
        { "POST", "/api/jobs", AssignedTechnician, 403, "auth.forbidden" },
        { "POST", "/api/jobs", OtherTechnician, 403, "auth.forbidden" },
        { "POST", "/api/jobs", Administrator, 201, null },
        { "GET", "/api/jobs", Anonymous, 401, "auth.unauthenticated" },
        { "GET", "/api/jobs", AssignedTechnician, 403, "auth.forbidden" },
        { "GET", "/api/jobs", OtherTechnician, 403, "auth.forbidden" },
        { "GET", "/api/jobs", Administrator, 200, null },
        { "GET", JobRoute, Anonymous, 401, "auth.unauthenticated" },
        { "GET", JobRoute, AssignedTechnician, 200, null },
        { "GET", JobRoute, OtherTechnician, 404, "job.not-found" },
        { "GET", JobRoute, Administrator, 200, null },
        { "PUT", AssignmentRoute, Anonymous, 401, "auth.unauthenticated" },
        { "PUT", AssignmentRoute, AssignedTechnician, 403, "auth.forbidden" },
        { "PUT", AssignmentRoute, OtherTechnician, 403, "auth.forbidden" },
        { "PUT", AssignmentRoute, Administrator, 200, null },
        { "PUT", StatusRoute, Anonymous, 401, "auth.unauthenticated" },
        { "PUT", StatusRoute, AssignedTechnician, 200, null },
        { "PUT", StatusRoute, OtherTechnician, 404, "job.not-found" },
        { "PUT", StatusRoute, Administrator, 403, "auth.forbidden" },
        { "POST", SubmissionRoute, Anonymous, 401, "auth.unauthenticated" },
        { "POST", SubmissionRoute, AssignedTechnician, 200, null },
        { "POST", SubmissionRoute, OtherTechnician, 404, "job.not-found" },
        { "POST", SubmissionRoute, Administrator, 403, "auth.forbidden" },
        { "POST", CancellationRoute, Anonymous, 401, "auth.unauthenticated" },
        { "POST", CancellationRoute, AssignedTechnician, 403, "auth.forbidden" },
        { "POST", CancellationRoute, OtherTechnician, 403, "auth.forbidden" },
        { "POST", CancellationRoute, Administrator, 200, null },
        { "GET", InspectionRoute, Anonymous, 401, "auth.unauthenticated" },
        { "GET", InspectionRoute, AssignedTechnician, 200, null },
        { "GET", InspectionRoute, OtherTechnician, 404, "job.not-found" },
        { "GET", InspectionRoute, Administrator, 200, null },
        { "PUT", InspectionRoute, Anonymous, 401, "auth.unauthenticated" },
        { "PUT", InspectionRoute, AssignedTechnician, 200, null },
        { "PUT", InspectionRoute, OtherTechnician, 404, "job.not-found" },
        { "PUT", InspectionRoute, Administrator, 403, "auth.forbidden" },
        { "POST", ClientErrorsRoute, Anonymous, 401, "auth.unauthenticated" },
        { "POST", ClientErrorsRoute, AssignedTechnician, 403, "auth.forbidden" },
        { "POST", ClientErrorsRoute, OtherTechnician, 403, "auth.forbidden" },
        { "POST", ClientErrorsRoute, Administrator, 202, null },
        { "GET", "/health/live", Anonymous, 200, null },
        { "GET", "/health/live", AssignedTechnician, 200, null },
        { "GET", "/health/live", OtherTechnician, 200, null },
        { "GET", "/health/live", Administrator, 200, null },
        { "GET", "/health/ready", Anonymous, 200, null },
        { "GET", "/health/ready", AssignedTechnician, 200, null },
        { "GET", "/health/ready", OtherTechnician, 200, null },
        { "GET", "/health/ready", Administrator, 200, null },
        { "GET", DocumentRoute, Anonymous, 200, null },
        { "GET", DocumentRoute, AssignedTechnician, 200, null },
        { "GET", DocumentRoute, OtherTechnician, 200, null },
        { "GET", DocumentRoute, Administrator, 200, null },
    };

    public static TheoryData<UserRole, SessionClient, int, string?> SignInByAccountAndClient => new()
    {
        { UserRole.Technician, SessionClient.Mobile, 200, null },
        { UserRole.Technician, SessionClient.Web, 403, "auth.wrong-application" },
        { UserRole.Administrator, SessionClient.Web, 200, null },
        { UserRole.Administrator, SessionClient.Mobile, 403, "auth.wrong-application" },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task EachCallerGetsTheOutcomeOfTheRoleMatrix(
        string method,
        string route,
        string caller,
        int expectedStatus,
        string? expectedCode)
    {
        var world = await PrepareAsync(method, route, await ArrangeAsync(Fixture));
        using var liveSession = await LiveSessionForAsync(route, caller, world);
        using var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = liveSession?.Session.AccessToken ?? world.TokenFor(Fixture.Factory, caller);

        using var response = await SendAsync(client, method, route, world);

        Assert.True(
            (int)response.StatusCode == expectedStatus,
            $"{method} {route} as {caller}: expected {expectedStatus}, got {(int)response.StatusCode}.");
        if (expectedCode is null)
        {
            return;
        }

        Assert.Equal(ApiClient.ProblemMediaType, response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(expectedCode, problem.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [MemberData(nameof(SignInByAccountAndClient))]
    public async Task SignInAcceptsEachRoleOnlyThroughItsOwnClient(
        UserRole role,
        SessionClient sessionClient,
        int expectedStatus,
        string? expectedCode)
    {
        var data = new TestData(Fixture, Fixture.Factory.Clock.GetUtcNow());
        var user = role == UserRole.Administrator
            ? await data.CreateAdministratorAsync(CancellationToken)
            : (await data.CreateTechnicianAsync(Trade.Plumbing, CancellationToken)).User;
        using var client = Fixture.Factory.CreateApiClient();

        using var response = await client.SignInAsync(user.Email, TestData.Password, sessionClient);

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        if (expectedCode is not null)
        {
            using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Forbidden, expectedCode);
            Assert.Empty(ApiClient.SetCookieHeadersOf(response));
        }
    }

    [Theory]
    [MemberData(nameof(InvalidAdministratorRequests))]
    public async Task AnInvalidRequestToAnAdministratorOnlyRouteGets401Or403BeforeValidationAndChangesNothing(
        string request,
        string caller,
        int expectedStatus,
        string expectedCode)
    {
        var world = await ArrangeAsync(Fixture);
        using var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = world.TokenFor(Fixture.Factory, caller);
        var before = await LoadJobAsync(world.JobId);

        using var response = await SendInvalidAsync(client, request, world);

        using var problem = await ApiClient.AssertProblemAsync(response, (HttpStatusCode)expectedStatus, expectedCode);
        if (expectedStatus == 400)
        {
            Assert.Equal(
                _invalidAdministratorRequests[request],
                problem.RootElement.GetProperty("errors").EnumerateObject().Select(member => member.Name).Order(StringComparer.Ordinal));
        }
        else
        {
            Assert.False(problem.RootElement.TryGetProperty("errors", out _), $"{request} as {caller} reached validation.");
        }

        var after = await LoadJobAsync(world.JobId);
        Assert.Equal((JobStatus.Assigned, before.Version), (after.Status, after.Version));
        Assert.Empty(Fixture.Factory.LogSink.Named(SecurityLog.ClientErrorReportedEvent));
        Fixture.Factory.Events.AssertEmpty();
    }

    [Fact]
    public async Task AnAdministratorTokenWithoutALiveSessionGets401SessionExpiredFromTheSocketTicketEndpoint()
    {
        var world = await ArrangeAsync(Fixture);
        using var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = world.TokenFor(Fixture.Factory, Administrator);

        using var ticket = await SendAsync(client, "POST", SocketTicketsRoute, world);
        using var jobs = await SendAsync(client, "GET", "/api/jobs", world);

        using var problem = await ApiClient.AssertProblemAsync(ticket, HttpStatusCode.Unauthorized, "auth.session-expired");
        Assert.Equal(HttpStatusCode.OK, jobs.StatusCode);
    }

    [Fact]
    public void EveryEndpointOfTheTestingInventoryHasARowForEachCallerAndEveryRowHasAnEndpoint()
    {
        var inventory = EndpointInventoryTests.ReadInventory(Fixture.Factory);
        var rows = Matrix
            .Select(row => (Method: row.Data.Item1, Route: row.Data.Item2, Caller: row.Data.Item3))
            .ToList();

        var missing = inventory
            .SelectMany(endpoint => _callers
                .Where(caller => !rows.Any(row =>
                    row.Route == endpoint.Route
                    && row.Caller == caller
                    && endpoint.Method.Split(',').Contains(row.Method)))
                .Select(caller => $"{endpoint} as {caller}"))
            .ToList();
        var unmapped = rows
            .Where(row => !inventory.Any(endpoint =>
                endpoint.Route == row.Route && endpoint.Method.Split(',').Contains(row.Method)))
            .Select(row => $"{row.Method} {row.Route}")
            .Distinct()
            .ToList();

        Assert.True(missing.Count == 0, "Endpoints without a role-matrix row:\n" + string.Join("\n", missing));
        Assert.True(unmapped.Count == 0, "Role-matrix rows without an endpoint:\n" + string.Join("\n", unmapped));
    }

    public static object CreateJobBody(DateTimeOffset now, string marker)
    {
        var start = UtcInstant.Truncate(now).AddDays(1);
        return new
        {
            title = $"Matrix job {marker}",
            description = "Created by the security tests.",
            priority = "Normal",
            customerName = $"Test Customer {marker}",
            siteAddress = $"Test Site {marker}",
            scheduledStart = start,
            scheduledEnd = start.AddHours(2),
            checklistTemplateId = TestData.HvacTemplateId,
        };
    }

    public static string DashboardWindow(DateTimeOffset now)
    {
        var from = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        return $"from={ApiClient.WireInstant(from)}&to={ApiClient.WireInstant(from.AddDays(1))}";
    }

    public static object ClientErrorBody(DateTimeOffset now) => new
    {
        message = "Cannot read properties of undefined (reading 'number')",
        source = "app.errorHandler",
        route = "/jobs",
        correlationId = (string?)null,
        stack = (string?)null,
        occurredAt = ApiClient.WireInstant(now),
        appVersion = "1.0.0",
    };

    public static async Task<World> ArrangeAsync(DatabaseFixture fixture)
    {
        var data = new TestData(fixture, fixture.Factory.Clock.GetUtcNow());
        var cancellationToken = TestContext.Current.CancellationToken;
        var administrator = await data.CreateAdministratorAsync(cancellationToken);
        var assigned = await data.CreateTechnicianAsync(Trade.Hvac, cancellationToken);
        var other = await data.CreateTechnicianAsync(Trade.Hvac, cancellationToken);
        var job = await data.CreateJobAsync(administrator.Id, TestData.HvacTemplateId, cancellationToken);
        var assignedJob = await data.AssignJobAsync(job, assigned, cancellationToken);
        return new World(administrator, assigned.User, other.User, job.Id, assignedJob.Version, data.Now);
    }

    private async Task<World> PrepareAsync(string method, string route, World world)
    {
        if ((method, route) is not (("PUT", InspectionRoute) or ("POST", SubmissionRoute)))
        {
            return world;
        }

        var data = new TestData(Fixture, world.Now);
        var job = await data.ChangeStatusAsync(await LoadJobAsync(world.JobId), JobStatus.InProgress, CancellationToken);
        if (route == SubmissionRoute)
        {
            await data.SaveInspectionAsync(job, TestData.PassingAnswers(TestData.ChecklistOf(TestData.HvacTemplateId)), CancellationToken);
        }

        return world with { JobVersion = (await LoadJobAsync(world.JobId)).Version };
    }

    private async Task<Job> LoadJobAsync(Guid jobId)
    {
        await using var db = Fixture.CreateAppDbContext();
        return await db.Jobs.AsNoTracking().SingleAsync(entry => entry.Id == jobId, CancellationToken);
    }

    private async Task<TestData.SignedInSession?> LiveSessionForAsync(string route, string caller, World world)
    {
        if (route != SocketTicketsRoute || caller != Administrator)
        {
            return null;
        }

        return await new TestData(Fixture, world.Now).SignInAsync(Fixture.Factory, SessionClient.Web, CancellationToken);
    }

    private static Task<HttpResponseMessage> SendAsync(ApiClient client, string method, string route, World world)
    {
        var path = route
            .Replace("{jobId:guid}", world.JobId.ToString(), StringComparison.Ordinal)
            .Replace("{documentName}", "v1", StringComparison.Ordinal);
        return (method, route) switch
        {
            ("POST", "/api/auth/sign-in") => client.SignInAsync(
                $"nobody-{Guid.NewGuid():N}@fieldservice.example",
                TestData.Password,
                SessionClient.Web),
            ("POST", "/api/auth/refresh") => client.PostAsync(path),
            ("POST", "/api/auth/sign-out") => client.PostAsync(path),
            ("POST", SocketTicketsRoute) => client.PostAsync(path),
            ("GET", DashboardRoute) => client.GetAsync($"{path}?{DashboardWindow(world.Now)}"),
            ("POST", CancellationRoute) => client.PostJsonAsync(path, new { reason = TestData.CancellationReason, version = world.JobVersion }),
            ("POST", ClientErrorsRoute) => client.PostJsonAsync(path, ClientErrorBody(world.Now)),
            ("POST", "/api/jobs") => client.PostJsonAsync(path, CreateJobBody(world.Now, Guid.NewGuid().ToString("N")[..8])),
            ("PUT", AssignmentRoute) => client.PutJsonAsync(path, new { technicianId = world.AssignedTechnician.Id, version = 0u }),
            ("PUT", StatusRoute) => client.PutJsonAsync(path, new { status = "InProgress", note = (string?)null, version = world.JobVersion }),
            ("POST", SubmissionRoute) => client.PostJsonAsync(path, new { technicianNotes = (string?)null, siteSafetyConfirmed = true, version = world.JobVersion }),
            ("PUT", InspectionRoute) => client.PutJsonAsync(path, new
            {
                answers = new[] { new { checklistItemId = TestData.ChecklistOf(TestData.HvacTemplateId).Items[0].Id, result = "Pass" } },
                version = world.JobVersion,
            }),
            ("GET", _) => client.GetAsync(path),
            _ => throw new ArgumentOutOfRangeException(nameof(route), $"{method} {route} has no request in the role matrix."),
        };
    }

    private static Task<HttpResponseMessage> SendInvalidAsync(ApiClient client, string request, World world)
    {
        var cancellation = CancellationRoute.Replace("{jobId:guid}", world.JobId.ToString(), StringComparison.Ordinal);
        var dayStart = new DateTimeOffset(world.Now.UtcDateTime.Date, TimeSpan.Zero);
        return request switch
        {
            "GetDashboardSummary without from and to" => client.GetAsync(DashboardRoute),
            "GetDashboardSummary with to before from" => client.GetAsync(
                $"{DashboardRoute}?from={ApiClient.WireInstant(dayStart.AddDays(1))}&to={ApiClient.WireInstant(dayStart)}"),
            "CancelJob with an empty object" => client.PostAsync(cancellation, JsonText("{}")),
            "CancelJob with a body that is not JSON" => client.PostAsync(cancellation, JsonText("{\"reason\":\"The customer cancelled the visit.\",")),
            "CancelJob with a reason of 501 characters" => client.PostJsonAsync(
                cancellation,
                new { reason = new string('r', 501), version = world.JobVersion }),
            "CancelJob without a body" => client.PostAsync(cancellation),
            "ReportClientError with an empty object" => client.PostAsync(ClientErrorsRoute, JsonText("{}")),
            "ReportClientError with a route holding a query string" => client.PostJsonAsync(ClientErrorsRoute, new
            {
                message = "Cannot read properties of undefined (reading 'number')",
                source = "app.errorHandler",
                route = "/jobs?status=Assigned",
                occurredAt = ApiClient.WireInstant(world.Now),
            }),
            "ReportClientError with a body that is not JSON" => client.PostAsync(ClientErrorsRoute, JsonText("message=Boom")),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request, "Unknown invalid request."),
        };
    }

    private static StringContent JsonText(string body) => new(body, Encoding.UTF8, "application/json");

    public sealed record World(
        User Administrator,
        User AssignedTechnician,
        User OtherTechnician,
        Guid JobId,
        uint JobVersion,
        DateTimeOffset Now)
    {
        public string? TokenFor(ApiFactory factory, string caller) => caller switch
        {
            RoleMatrixTests.Anonymous => null,
            RoleMatrixTests.AssignedTechnician => TestTokens.For(factory, AssignedTechnician),
            RoleMatrixTests.OtherTechnician => TestTokens.For(factory, OtherTechnician),
            RoleMatrixTests.Administrator => TestTokens.For(factory, Administrator),
            _ => throw new ArgumentOutOfRangeException(nameof(caller), caller, "Unknown caller."),
        };
    }
}
