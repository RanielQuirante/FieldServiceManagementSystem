using System.Reflection;
using System.Text;
using FieldService.Api.Domain;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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

    private const string HttpsOrigin = "https://fieldservice.example";

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
        new(AnyMethod, "/health/live", Anonymous, Required: true),
        new(AnyMethod, "/health/ready", Anonymous, Required: true),
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

        var violations = inventory.SelectMany(DomainTypeViolations).ToList();

        Assert.True(violations.Count == 0, Describe(ApiFactory.TestingEnvironment, inventory, violations));
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
            "GET /api/inventory-probe/domain-types: is not in the expected inventory for Testing",
            $"GET /api/inventory-probe/domain-types: parameter status uses {typeof(JobStatus).FullName}",
            $"GET /api/inventory-probe/domain-types: result type uses {typeof(JobPriority).FullName}",
        ];

        var inventory = ReadInventory(factory);
        var violations = FindViolations(inventory, ApiFactory.TestingEnvironment)
            .Concat(inventory.SelectMany(DomainTypeViolations))
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
            "* /health/live: is required in Staging but is not mapped",
            "* /health/ready: is required in Staging but is not mapped",
        ];

        var violations = FindViolations(inventory, "Staging");

        Assert.Equal(expected, violations);
    }

    internal static void ConfigureEnvironment(ApiFactory factory, string environment)
    {
        factory.EnvironmentName = environment;
        factory.Settings["Logs:FileEnabled"] = "false";
        if (environment == "Production")
        {
            factory.Settings["Cors:AllowedOrigins:0"] = HttpsOrigin;
        }
    }

    public static IReadOnlyList<ActualEndpoint> ReadInventory(ApiFactory factory)
    {
        var source = factory.Services.GetRequiredService<EndpointDataSource>();
        return [.. source.Endpoints.Select(ActualEndpoint.From)];
    }

    private static List<string> FindViolations(IReadOnlyList<ActualEndpoint> inventory, string environment)
    {
        var expected = _expected.Where(entry => entry.AppliesTo(environment)).ToList();
        var violations = new List<string>();
        foreach (var endpoint in inventory)
        {
            var entry = expected.FirstOrDefault(candidate => candidate.Matches(endpoint));
            if (!endpoint.IsAnonymous && endpoint.Policies.Length == 0)
            {
                violations.Add($"{endpoint}: has no explicit policy and is not on the anonymous allowlist");
            }

            if (!endpoint.IsAnonymous && endpoint.UnnamedAuthorization)
            {
                violations.Add($"{endpoint}: requires authorization without a named policy");
            }

            if (entry is null)
            {
                if (endpoint.IsAnonymous)
                {
                    violations.Add($"{endpoint}: allows anonymous access but is not on the anonymous allowlist");
                }

                violations.Add($"{endpoint}: is not in the expected inventory for {environment}");
                continue;
            }

            if (entry.Access == Anonymous && !endpoint.IsAnonymous)
            {
                violations.Add($"{endpoint}: is on the anonymous allowlist but does not allow anonymous access");
            }
            else if (entry.Access != Anonymous && endpoint.IsAnonymous)
            {
                violations.Add(
                    $"{endpoint}: allows anonymous access but is not on the anonymous allowlist; expected policy {entry.Access}");
            }
            else if (entry.Access != Anonymous && !endpoint.Policies.SequenceEqual([entry.Access]))
            {
                violations.Add(
                    $"{endpoint}: has policies [{string.Join(", ", endpoint.Policies)}] where [{entry.Access}] is expected");
            }
        }

        foreach (var entry in expected.Where(entry => entry.Required && !inventory.Any(entry.Matches)))
        {
            violations.Add($"{entry.Method} {entry.Route}: is required in {environment} but is not mapped");
        }

        return violations;
    }

    private static IEnumerable<string> DomainTypeViolations(ActualEndpoint endpoint)
    {
        if (endpoint.Handler is null)
        {
            yield break;
        }

        foreach (var parameter in endpoint.Handler.GetParameters())
        {
            var types = Flatten(parameter.ParameterType);
            if (parameter.GetCustomAttribute<AsParametersAttribute>() is not null)
            {
                types = types.Concat(parameter.ParameterType
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .SelectMany(property => Flatten(property.PropertyType)));
            }

            foreach (var type in types.Where(IsDomainType).Distinct())
            {
                yield return $"{endpoint}: parameter {parameter.Name} uses {type.FullName}";
            }
        }

        foreach (var type in Flatten(endpoint.Handler.ReturnType).Where(IsDomainType).Distinct())
        {
            yield return $"{endpoint}: result type uses {type.FullName}";
        }
    }

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

    private static bool IsDomainType(Type type)
    {
        return type.Namespace is { } name
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
        endpoints.MapGet($"{ProbePrefix}domain-types", (JobStatus status) => TypedResults.Ok(JobPriority.High))
            .RequireAuthorization(Administrator);
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
