using System.Reflection;
using System.Text;
using FieldService.Api.Domain;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace FieldService.Api.Tests.Security;

[Collection(DatabaseCollection.Name)]
public sealed class EndpointInventoryTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string Anonymous = "(anonymous)";

    private const string Administrator = "Administrator";

    private const string Technician = "Technician";

    private const string AdministratorOrTechnician = "AdministratorOrTechnician";

    private const string AnyMethod = "*";

    private const string DomainNamespace = "FieldService.Api.Domain";

    private const string FeaturesNamespace = "FieldService.Api.Features";

    private const string HttpsOrigin = "https://fieldservice.example";

    private const string NginxAddress = "172.30.80.10";

    private const string ProbePrefix = "/api/inventory-probe/";

    private static readonly string[] _documentEnvironments = ["Development", "Testing"];

    private static readonly ExpectedEndpoint[] _expected =
    [
        new("POST", "/api/auth/sign-in", Anonymous),
        new("POST", "/api/auth/refresh", Anonymous),
        new("POST", "/api/auth/sign-out", Anonymous),
        new("POST", "/api/auth/socket-tickets", Administrator),
        new("GET", "/api/dashboard/summary", Administrator),
        new("GET", "/api/technicians", Administrator),
        new("GET", "/api/technicians/me/jobs", Technician),
        new("POST", "/api/jobs", Administrator),
        new("GET", "/api/jobs", Administrator),
        new("GET", "/api/jobs/{jobId:guid}", AdministratorOrTechnician),
        new("PUT", "/api/jobs/{jobId:guid}/assignment", Administrator),
        new("PUT", "/api/jobs/{jobId:guid}/status", Technician),
        new("POST", "/api/jobs/{jobId:guid}/submission", Technician),
        new("POST", "/api/jobs/{jobId:guid}/cancellation", Administrator),
        new("GET", "/api/jobs/{jobId:guid}/inspection", AdministratorOrTechnician),
        new("PUT", "/api/jobs/{jobId:guid}/inspection", Technician),
        new("GET", "/api/checklist-templates", Administrator),
        new("POST", "/api/client-errors", Administrator),
        new("GET,HEAD", "/health/live", Anonymous, Required: true),
        new("GET,HEAD", "/health/ready", Anonymous, Required: true),
        new("GET", "/openapi/{documentName}.json", Anonymous, Required: true, Environments: _documentEnvironments),
    ];

    [Theory]
    [InlineData("Testing")]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public async Task EveryEndpointHasAnExplicitPolicyOrIsOnTheAnonymousAllowlist(string environment)
    {
        await using var factory = Fixture.CreateFactory(factory => ConfigureEnvironment(factory, environment));

        var inventory = ReadInventory(factory);

        var violations = FindViolations(inventory, environment);
        Assert.True(violations.Count == 0, Describe(environment, inventory, violations));
    }

    [Fact]
    public void NoEndpointParameterOrResultTypeComesFromTheDomainNamespace()
    {
        var inventory = ReadInventory(Fixture.Factory);
        var services = ServicesOf(Fixture.Factory);

        var violations = inventory.SelectMany(endpoint => DomainTypeViolations(endpoint, services)).ToList();

        Assert.True(violations.Count == 0, Describe(ApiFactory.TestingEnvironment, inventory, violations));
    }

    [Fact]
    public void TheDomainRuleReachesEveryRequestAndResponseRecordUnderFeatures()
    {
        var inventory = ReadInventory(Fixture.Factory);
        var services = ServicesOf(Fixture.Factory);
        var records = typeof(Program).Assembly
            .GetTypes()
            .Where(type => type.IsPublic && type.Namespace?.StartsWith(FeaturesNamespace, StringComparison.Ordinal) == true)
            .Where(type => type.GetMethod("<Clone>$") is not null)
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToList();

        var reached = inventory
            .SelectMany(endpoint => ContractTypes(endpoint, services))
            .Select(type => type.FullName!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(records);
        var unreached = records.Where(record => !reached.Contains(record)).ToList();
        Assert.True(unreached.Count == 0, "Records under Features that no endpoint parameter or result reaches:\n" + string.Join("\n", unreached));
    }

    [Fact]
    public async Task InventoryReadsTestOnlyEndpointsAndFlagsEachOneThatBreaksTheRules()
    {
        await using var factory = Fixture.CreateFactory(factory => factory.TestServices = services =>
            services.AddSingleton<IStartupFilter>(new ProbeEndpointsStartupFilter(MapProbeEndpoints)));
        string[] expected =
        [
            "GET /api/inventory-probe/no-policy: has no explicit policy and is not on the anonymous allowlist",
            "GET /api/inventory-probe/no-policy: is not in the expected inventory for Testing",
            "GET /api/inventory-probe/default-policy: has no explicit policy and is not on the anonymous allowlist",
            "GET /api/inventory-probe/default-policy: requires authorization without a named policy",
            "GET /api/inventory-probe/default-policy: is not in the expected inventory for Testing",
            "GET /api/inventory-probe/anonymous: allows anonymous access but is not on the anonymous allowlist",
            "GET /api/inventory-probe/anonymous: is not in the expected inventory for Testing",
            "POST /api/inventory-probe/domain-entity-parameter: is not in the expected inventory for Testing",
            $"POST /api/inventory-probe/domain-entity-parameter: parameter job uses {typeof(Job).FullName}",
            "POST /api/inventory-probe/domain-entity-member: is not in the expected inventory for Testing",
            $"POST /api/inventory-probe/domain-entity-member: parameter request uses {typeof(Technician).FullName}",
            "GET /api/inventory-probe/domain-entity-result: is not in the expected inventory for Testing",
            $"GET /api/inventory-probe/domain-entity-result: result type uses {typeof(Job).FullName}",
            "GET /api/inventory-probe/domain-enumerations: is not in the expected inventory for Testing",
            "POST /api/inventory-probe/domain-entity-in-body-member: is not in the expected inventory for Testing",
            $"POST /api/inventory-probe/domain-entity-in-body-member: parameter request member ProbeAssignment.Assignee uses {typeof(Technician).FullName}",
            "GET /api/inventory-probe/domain-entity-in-response-member: is not in the expected inventory for Testing",
            $"GET /api/inventory-probe/domain-entity-in-response-member: result type member ProbeHistory.Entry uses {typeof(JobHistoryEntry).FullName}",
            $"GET /api/inventory-probe/domain-entity-in-response-member: result type member ProbeJobResponse.Tags uses {typeof(ChecklistItem).FullName}",
            "GET /api/inventory-probe/domain-enumerations-in-members: is not in the expected inventory for Testing",
        ];

        var services = ServicesOf(factory);
        var probes = ReadInventory(factory).Where(IsProbe).ToList();
        var violations = probes
            .SelectMany(endpoint => EndpointViolations(endpoint, ExpectedFor(ApiFactory.TestingEnvironment))
                .Concat(DomainTypeViolations(endpoint, services)))
            .ToList();

        Assert.Equal(expected.Order(StringComparer.Ordinal), violations.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void InventoryRulesReportEachKindOfViolation()
    {
        ActualEndpoint[] inventory =
        [
            new("POST", "/api/auth/sign-in", IsAnonymous: false, [], UnnamedAuthorization: false),
            new("GET", "/api/jobs", IsAnonymous: true, [], UnnamedAuthorization: false),
            new("GET", "/api/jobs/{jobId:guid}", IsAnonymous: false, [Administrator], UnnamedAuthorization: false),
            new("PUT", "/api/jobs/{jobId}/status", IsAnonymous: false, [Technician], UnnamedAuthorization: false),
            new("GET", "/api/technicians", IsAnonymous: false, [], UnnamedAuthorization: true),
            new("GET", "/openapi/{documentName}.json", IsAnonymous: true, [], UnnamedAuthorization: false),
            new(AnyMethod, "/health/live", IsAnonymous: true, [], UnnamedAuthorization: false),
            new("GET", "/health/ready", IsAnonymous: true, [], UnnamedAuthorization: false),
        ];
        string[] expected =
        [
            "POST /api/auth/sign-in: has no explicit policy and is not on the anonymous allowlist",
            "POST /api/auth/sign-in: is on the anonymous allowlist but does not allow anonymous access",
            "GET /api/jobs: allows anonymous access but is not on the anonymous allowlist; expected policy Administrator",
            "GET /api/jobs/{jobId:guid}: has policies [Administrator] where [AdministratorOrTechnician] is expected",
            "PUT /api/jobs/{jobId}/status: is not in the expected inventory for Staging",
            "GET /api/technicians: has no explicit policy and is not on the anonymous allowlist",
            "GET /api/technicians: requires authorization without a named policy",
            "GET /api/technicians: has policies [] where [Administrator] is expected",
            "GET /openapi/{documentName}.json: allows anonymous access but is not on the anonymous allowlist",
            "GET /openapi/{documentName}.json: is not in the expected inventory for Staging",
            "* /health/live: allows anonymous access but is not on the anonymous allowlist",
            "* /health/live: is not in the expected inventory for Staging",
            "GET /health/ready: allows anonymous access but is not on the anonymous allowlist",
            "GET /health/ready: is not in the expected inventory for Staging",
            "GET,HEAD /health/live: is required in Staging but is not mapped",
            "GET,HEAD /health/ready: is required in Staging but is not mapped",
        ];

        var violations = FindViolations(inventory, "Staging");

        Assert.Equal(expected, violations);
    }

    internal static void ConfigureEnvironment(ApiFactory factory, string environment)
    {
        factory.EnvironmentName = environment;
        factory.Settings["Logs:FileEnabled"] = "false";
        if (environment is "Staging" or "Production")
        {
            factory.Settings[$"{ApiFactory.KnownProxiesKey}:0"] = NginxAddress;
        }

        if (environment == "Production")
        {
            factory.Settings["Cors:AllowedOrigins:0"] = HttpsOrigin;
            factory.Settings["AllowedHosts"] = factory.ClientOptions.BaseAddress.Host;
        }
    }

    public static IReadOnlyList<ActualEndpoint> ReadInventory(ApiFactory factory)
    {
        var source = factory.Services.GetRequiredService<EndpointDataSource>();
        return [.. source.Endpoints.Select(ActualEndpoint.From)];
    }

    private static List<string> FindViolations(IReadOnlyList<ActualEndpoint> inventory, string environment)
    {
        var expected = ExpectedFor(environment);
        var violations = inventory.SelectMany(endpoint => EndpointViolations(endpoint, expected)).ToList();
        foreach (var entry in expected.Entries.Where(entry => entry.Required && !inventory.Any(entry.Matches)))
        {
            violations.Add($"{entry.Method} {entry.Route}: is required in {environment} but is not mapped");
        }

        return violations;
    }

    private static ExpectedInventory ExpectedFor(string environment)
    {
        return new ExpectedInventory(environment, [.. _expected.Where(entry => entry.AppliesTo(environment))]);
    }

    private static IEnumerable<string> EndpointViolations(ActualEndpoint endpoint, ExpectedInventory expected)
    {
        var entry = expected.Entries.FirstOrDefault(candidate => candidate.Matches(endpoint));
        if (!endpoint.IsAnonymous && endpoint.Policies.Length == 0)
        {
            yield return $"{endpoint}: has no explicit policy and is not on the anonymous allowlist";
        }

        if (!endpoint.IsAnonymous && endpoint.UnnamedAuthorization)
        {
            yield return $"{endpoint}: requires authorization without a named policy";
        }

        if (entry is null)
        {
            if (endpoint.IsAnonymous)
            {
                yield return $"{endpoint}: allows anonymous access but is not on the anonymous allowlist";
            }

            yield return $"{endpoint}: is not in the expected inventory for {expected.Environment}";
            yield break;
        }

        if (entry.Access == Anonymous && !endpoint.IsAnonymous)
        {
            yield return $"{endpoint}: is on the anonymous allowlist but does not allow anonymous access";
        }
        else if (entry.Access != Anonymous && endpoint.IsAnonymous)
        {
            yield return
                $"{endpoint}: allows anonymous access but is not on the anonymous allowlist; expected policy {entry.Access}";
        }
        else if (entry.Access != Anonymous && !endpoint.Policies.SequenceEqual([entry.Access]))
        {
            yield return
                $"{endpoint}: has policies [{string.Join(", ", endpoint.Policies)}] where [{entry.Access}] is expected";
        }
    }

    private static bool IsProbe(ActualEndpoint endpoint)
    {
        return endpoint.Route.StartsWith(ProbePrefix, StringComparison.Ordinal);
    }

    private static IServiceProviderIsService ServicesOf(ApiFactory factory) =>
        factory.Services.GetRequiredService<IServiceProviderIsService>();

    private static IEnumerable<string> DomainTypeViolations(ActualEndpoint endpoint, IServiceProviderIsService services)
    {
        if (endpoint.Handler is null)
        {
            yield break;
        }

        foreach (var parameter in endpoint.Handler.GetParameters())
        {
            var types = ParameterRoots(parameter).ToList();
            foreach (var type in types.Where(IsRefusedDomainType).Distinct())
            {
                yield return $"{endpoint}: parameter {parameter.Name} uses {type.FullName}";
            }

            if (services.IsService(parameter.ParameterType))
            {
                continue;
            }

            var visited = new HashSet<Type>();
            foreach (var root in WalkRoots(parameter, types, services))
            {
                foreach (var member in MemberViolations(root, services, visited))
                {
                    yield return $"{endpoint}: parameter {parameter.Name} member {member}";
                }
            }
        }

        var results = Flatten(endpoint.Handler.ReturnType).ToList();
        foreach (var type in results.Where(IsRefusedDomainType).Distinct())
        {
            yield return $"{endpoint}: result type uses {type.FullName}";
        }

        var resultVisited = new HashSet<Type>();
        foreach (var root in results.Where(type => IsWalkedRecord(type, services)).Distinct())
        {
            foreach (var member in MemberViolations(root, services, resultVisited))
            {
                yield return $"{endpoint}: result type member {member}";
            }
        }
    }

    private static IEnumerable<Type> ContractTypes(ActualEndpoint endpoint, IServiceProviderIsService services)
    {
        if (endpoint.Handler is null)
        {
            return [];
        }

        var reached = new HashSet<Type>();
        foreach (var parameter in endpoint.Handler.GetParameters().Where(parameter => !services.IsService(parameter.ParameterType)))
        {
            if (IsAsParameters(parameter))
            {
                reached.Add(parameter.ParameterType);
            }

            foreach (var root in WalkRoots(parameter, [.. ParameterRoots(parameter)], services))
            {
                Collect(root, services, reached);
            }
        }

        foreach (var root in Flatten(endpoint.Handler.ReturnType).Where(type => IsWalkedRecord(type, services)))
        {
            Collect(root, services, reached);
        }

        return reached;
    }

    private static IEnumerable<Type> ParameterRoots(ParameterInfo parameter)
    {
        var types = Flatten(parameter.ParameterType);
        return IsAsParameters(parameter)
            ? types.Concat(parameter.ParameterType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(property => Flatten(property.PropertyType)))
            : types;
    }

    private static IEnumerable<Type> WalkRoots(ParameterInfo parameter, List<Type> roots, IServiceProviderIsService services) =>
        roots
            .Where(type => !(IsAsParameters(parameter) && type == parameter.ParameterType))
            .Where(type => IsWalkedRecord(type, services))
            .Distinct();

    private static bool IsAsParameters(ParameterInfo parameter) =>
        parameter.GetCustomAttribute<AsParametersAttribute>() is not null;

    private static IEnumerable<string> MemberViolations(Type owner, IServiceProviderIsService services, HashSet<Type> visited)
    {
        if (!visited.Add(owner))
        {
            yield break;
        }

        foreach (var property in owner.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            foreach (var type in Flatten(property.PropertyType).Distinct())
            {
                if (IsRefusedDomainType(type))
                {
                    yield return $"{owner.Name}.{property.Name} uses {type.FullName}";
                }
                else if (IsWalkedRecord(type, services))
                {
                    foreach (var nested in MemberViolations(type, services, visited))
                    {
                        yield return nested;
                    }
                }
            }
        }
    }

    private static void Collect(Type owner, IServiceProviderIsService services, HashSet<Type> reached)
    {
        if (!reached.Add(owner))
        {
            return;
        }

        foreach (var property in owner.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var type in Flatten(property.PropertyType).Where(type => IsWalkedRecord(type, services)))
            {
                Collect(type, services, reached);
            }
        }
    }

    private static bool IsWalkedRecord(Type type, IServiceProviderIsService services) =>
        !type.IsEnum
        && !type.IsPrimitive
        && !type.HasElementType
        && !type.IsGenericTypeDefinition
        && !IsRefusedDomainType(type)
        && type.Namespace is { } name
        && !name.StartsWith(DomainNamespace, StringComparison.Ordinal)
        && (type.Assembly == typeof(Program).Assembly || type.Assembly == typeof(EndpointInventoryTests).Assembly)
        && !services.IsService(type);

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;
        if (type.HasElementType)
        {
            foreach (var element in Flatten(type.GetElementType()!))
            {
                yield return element;
            }
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments().SelectMany(Flatten))
            {
                yield return argument;
            }
        }
    }

    private static bool IsRefusedDomainType(Type type)
    {
        return !type.HasElementType
            && !type.IsEnum
            && type.Namespace is { } name
            && (name == DomainNamespace || name.StartsWith(DomainNamespace + ".", StringComparison.Ordinal));
    }

    private static string Describe(string environment, IReadOnlyList<ActualEndpoint> inventory, List<string> violations)
    {
        var text = new StringBuilder()
            .Append("Endpoint inventory in ")
            .Append(environment)
            .Append(" breaks the authorization rules: every endpoint needs a named policy or a place on the anonymous allowlist, and every route needs a reviewed entry in this test:\n");
        foreach (var violation in violations)
        {
            text.Append("  ").Append(violation).Append('\n');
        }

        text.Append("Endpoints read from EndpointDataSource:\n");
        foreach (var endpoint in inventory)
        {
            text.Append("  ").Append(endpoint).Append(' ').Append(endpoint.AccessText).Append('\n');
        }

        return text.ToString();
    }

    private static void MapProbeEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet($"{ProbePrefix}no-policy", () => TypedResults.Ok());
        endpoints.MapGet($"{ProbePrefix}default-policy", () => TypedResults.Ok()).RequireAuthorization();
        endpoints.MapGet($"{ProbePrefix}anonymous", () => TypedResults.Ok()).AllowAnonymous();
        endpoints.MapPost(
                $"{ProbePrefix}domain-entity-parameter",
                ([FromBody] Job job, JobStatus status) => TypedResults.NoContent())
            .RequireAuthorization(Administrator);
        endpoints.MapPost(
                $"{ProbePrefix}domain-entity-member",
                ([AsParameters] EntityProbeRequest request) => TypedResults.NoContent())
            .RequireAuthorization(Administrator);
        endpoints.MapGet(
                $"{ProbePrefix}domain-entity-result",
                Results<Ok<Job>, NotFound> (JobPriority priority) => TypedResults.NotFound())
            .RequireAuthorization(Administrator);
        endpoints.MapGet(
                $"{ProbePrefix}domain-enumerations",
                Results<Ok<JobPriority>, Ok<JobStatus?>, NotFound> (
                    JobStatus status,
                    JobPriority? priority,
                    [AsParameters] EnumerationProbeRequest request) => TypedResults.NotFound())
            .RequireAuthorization(Administrator);
        endpoints.MapPost(
                $"{ProbePrefix}domain-entity-in-body-member",
                ([FromBody] ProbeCreateRequest request) => TypedResults.NoContent())
            .RequireAuthorization(Administrator);
        endpoints.MapGet(
                $"{ProbePrefix}domain-entity-in-response-member",
                Results<Ok<ProbeJobResponse>, NotFound> () => TypedResults.NotFound())
            .RequireAuthorization(Administrator);
        endpoints.MapGet(
                $"{ProbePrefix}domain-enumerations-in-members",
                Task<Results<Ok<ProbeEnumerationResponse>, NotFound>> () =>
                    Task.FromResult<Results<Ok<ProbeEnumerationResponse>, NotFound>>(TypedResults.NotFound()))
            .RequireAuthorization(Administrator);
    }

    public sealed record ProbeCreateRequest(string Title, ProbeAssignment Assignment, JobPriority Priority);

    public sealed record ProbeAssignment(Technician? Assignee, Trade Trade);

    public sealed record ProbeJobResponse(Guid Id, IReadOnlyList<ProbeHistory> History, Dictionary<string, ChecklistItem[]> Tags);

    public sealed record ProbeHistory(JobHistoryEntry Entry, JobStatus Status, ProbeHistory? Previous);

    public sealed record ProbeEnumerationResponse(
        JobStatus Status,
        JobPriority? Priority,
        IReadOnlyList<UserRole> Roles,
        ProbeEnumerationResponse? Parent);

    public sealed record EntityProbeRequest
    {
        [FromBody]
        public Technician? Technician { get; init; }

        public Trade? Trade { get; init; }
    }

    public sealed record EnumerationProbeRequest
    {
        public UserRole Role { get; init; }

        public SessionClient? Client { get; init; }

        public JobHistoryAction[]? Actions { get; init; }
    }

    public sealed record ActualEndpoint(
        string Method,
        string Route,
        bool IsAnonymous,
        string[] Policies,
        bool UnnamedAuthorization,
        MethodInfo? Handler = null)
    {
        public string AccessText => IsAnonymous
            ? Anonymous
            : $"[{string.Join(", ", Policies)}]{(UnnamedAuthorization ? " with unnamed authorization" : string.Empty)}";

        public static ActualEndpoint From(Endpoint endpoint)
        {
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            var authorizeData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            return new ActualEndpoint(
                methods is { Count: > 0 } ? string.Join(",", methods.Order(StringComparer.Ordinal)) : AnyMethod,
                endpoint is RouteEndpoint route ? RouteText(route.RoutePattern) : $"(not a route: {endpoint.DisplayName})",
                endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null,
                [.. authorizeData
                    .Select(data => data.Policy)
                    .OfType<string>()
                    .Where(policy => policy.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)],
                authorizeData.Any(data => string.IsNullOrEmpty(data.Policy))
                    || endpoint.Metadata.GetMetadata<AuthorizationPolicy>() is not null,
                endpoint.Metadata.GetMetadata<MethodInfo>());
        }

        public override string ToString() => $"{Method} {Route}";

        private static string RouteText(RoutePattern pattern)
        {
            var text = new StringBuilder();
            foreach (var segment in pattern.PathSegments)
            {
                text.Append('/');
                foreach (var part in segment.Parts)
                {
                    text.Append(part switch
                    {
                        RoutePatternLiteralPart literal => literal.Content,
                        RoutePatternSeparatorPart separator => separator.Content,
                        RoutePatternParameterPart parameter => ParameterText(parameter),
                        _ => "?",
                    });
                }
            }

            return text.Length == 0 ? "/" : text.ToString();
        }

        private static string ParameterText(RoutePatternParameterPart parameter)
        {
            var constraints = string.Concat(parameter.ParameterPolicies.Select(policy => $":{policy.Content}"));
            var catchAll = parameter.IsCatchAll ? "*" : string.Empty;
            var optional = parameter.IsOptional ? "?" : string.Empty;
            return $"{{{catchAll}{parameter.Name}{constraints}{optional}}}";
        }
    }

    private sealed record ExpectedEndpoint(
        string Method,
        string Route,
        string Access,
        bool Required = false,
        string[]? Environments = null)
    {
        public bool AppliesTo(string environment) => Environments is null || Environments.Contains(environment);

        public bool Matches(ActualEndpoint endpoint) => endpoint.Method == Method && endpoint.Route == Route;
    }

    private sealed record ExpectedInventory(string Environment, ExpectedEndpoint[] Entries);

    private sealed class ProbeEndpointsStartupFilter(Action<IEndpointRouteBuilder> map) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                next(app);
                app.UseRouting();
                app.UseEndpoints(map);
            };
        }
    }
}
