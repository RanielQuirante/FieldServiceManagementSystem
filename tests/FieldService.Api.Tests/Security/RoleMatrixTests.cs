using System.Net;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Tests.Infrastructure;

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

    private const string TechniciansRoute = "/api/technicians";

    private const string MyJobsRoute = "/api/technicians/me/jobs";

    private const string DocumentRoute = "/openapi/{documentName}.json";

    private static readonly string[] _callers = [Anonymous, AssignedTechnician, OtherTechnician, Administrator];

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
        var world = await ArrangeAsync(Fixture);
        using var client = Fixture.Factory.CreateApiClient();
        client.AccessToken = world.TokenFor(Fixture.Factory, caller);

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

    public static async Task<World> ArrangeAsync(DatabaseFixture fixture)
    {
        var data = new TestData(fixture, fixture.Factory.Clock.GetUtcNow());
        var cancellationToken = TestContext.Current.CancellationToken;
        var administrator = await data.CreateAdministratorAsync(cancellationToken);
        var assigned = await data.CreateTechnicianAsync(Trade.Hvac, cancellationToken);
        var other = await data.CreateTechnicianAsync(Trade.Hvac, cancellationToken);
        var job = await data.CreateJobAsync(administrator.Id, TestData.HvacTemplateId, cancellationToken);
        await data.AssignJobAsync(job, assigned, cancellationToken);
        return new World(administrator, assigned.User, other.User, job.Id, data.Now);
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
            ("POST", "/api/jobs") => client.PostJsonAsync(path, CreateJobBody(world.Now, Guid.NewGuid().ToString("N")[..8])),
            ("PUT", AssignmentRoute) => client.PutJsonAsync(path, new { technicianId = world.AssignedTechnician.Id, version = 0u }),
            ("GET", _) => client.GetAsync(path),
            _ => throw new ArgumentOutOfRangeException(nameof(route), $"{method} {route} has no request in the role matrix."),
        };
    }

    public sealed record World(
        User Administrator,
        User AssignedTechnician,
        User OtherTechnician,
        Guid JobId,
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
