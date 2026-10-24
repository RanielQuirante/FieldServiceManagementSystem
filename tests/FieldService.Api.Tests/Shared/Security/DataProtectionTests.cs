using System.Net;
using FieldService.Api.Domain;
using FieldService.Api.Tests.Features.Auth;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog.Events;

namespace FieldService.Api.Tests.Shared.Security;

[Collection(DatabaseCollection.Name)]
public sealed class DataProtectionTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string DataProtectionNamespace = "Microsoft.AspNetCore.DataProtection";

    private const string AuthenticationNamespace = "Microsoft.AspNetCore.Authentication";

    private const string ObsoleteSystemClock = "Microsoft.AspNetCore.Authentication.ISystemClock";

    private const string AuthenticationSection = "Authentication";

    private const string OverrideKey = "Serilog:MinimumLevel:Override:";

    [Fact]
    public async Task TheApiRegistersNoDataProtectionServiceSoNoKeyRingCanBeCreated()
    {
        var registered = new List<ServiceDescriptor>();
        await using var factory = Fixture.CreateFactory(factory => factory.TestServices = services => registered.AddRange(services));

        var provider = factory.Services;

        Assert.NotEmpty(registered);
        Assert.Empty(registered.Where(descriptor => TypesOf(descriptor).Any(IsDataProtection)).Select(Describe));
        Assert.Null(provider.GetService<IDataProtectionProvider>());
        Assert.DoesNotContain(provider.GetServices<IHostedService>(), service => IsDataProtection(service.GetType()));
    }

    [Fact]
    public async Task AuthenticationKeepsEveryServiceOfTheFrameworkRegistrationApartFromDataProtection()
    {
        var reference = new ServiceCollection();
        reference.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        var registered = new List<ServiceDescriptor>();
        await using var factory = Fixture.CreateFactory(factory => factory.TestServices = services => registered.AddRange(services));

        var provider = factory.Services;

        var missing = reference
            .Where(descriptor => !TypesOf(descriptor).Any(IsDataProtection))
            .Where(descriptor => descriptor.ServiceType.FullName != ObsoleteSystemClock)
            .Where(descriptor => !registered.Any(candidate => Provides(candidate, descriptor)))
            .Select(Describe)
            .ToList();
        Assert.Empty(missing);
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();
        var authenticate = await schemes.GetDefaultAuthenticateSchemeAsync();
        var challenge = await schemes.GetDefaultChallengeSchemeAsync();
        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, authenticate?.Name);
        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, challenge?.Name);
        Assert.Equal(typeof(JwtBearerHandler), authenticate?.HandlerType);
        Assert.Equal([JwtBearerDefaults.AuthenticationScheme], (await schemes.GetAllSchemesAsync()).Select(scheme => scheme.Name));
    }

    [Fact]
    public async Task TheBearerSchemeReadsTheSameConfigurationSectionAsTheFrameworkDefault()
    {
        var factory = Fixture.Factory;
        var configuration = factory.Services.GetRequiredService<IConfiguration>();
        var reference = new ServiceCollection().AddSingleton(configuration);
        reference.AddAuthentication();
        await using var referenceProvider = reference.BuildServiceProvider();

        var actual = factory.Services.GetRequiredService<IAuthenticationConfigurationProvider>().AuthenticationConfiguration;
        var expected = referenceProvider.GetRequiredService<IAuthenticationConfigurationProvider>().AuthenticationConfiguration;

        var actualSection = Assert.IsAssignableFrom<IConfigurationSection>(actual);
        var expectedSection = Assert.IsAssignableFrom<IConfigurationSection>(expected);
        Assert.Equal(AuthenticationSection, expectedSection.Path);
        Assert.Equal(expectedSection.Path, actualSection.Path);
    }

    [Fact]
    public async Task AStagingHostServesAWholeSessionWithoutAnyDataProtectionLogEvent()
    {
        await using var factory = Fixture.CreateFactory(factory =>
        {
            factory.EnvironmentName = Environments.Staging;
            factory.Settings["Logs:FileEnabled"] = "false";
            factory.Settings[$"{OverrideKey}{DataProtectionNamespace}"] = nameof(LogEventLevel.Verbose);
            factory.Settings[$"{OverrideKey}{AuthenticationNamespace}"] = nameof(LogEventLevel.Verbose);
        });
        var data = new TestData(Fixture, factory.Clock.GetUtcNow());
        using var signedIn = await data.SignInAsync(factory, SessionClient.Web, CancellationToken);
        signedIn.Client.AccessToken = signedIn.Session.AccessToken;

        using var technicians = await signedIn.Client.GetAsync("/api/technicians");
        using var ticket = await signedIn.Client.PostAsync(IssueSocketTicketTests.Path);
        using var refreshed = await signedIn.RefreshAsync();
        using var signedOut = await signedIn.SignOutAsync(await signedIn.NextTokenAsync(refreshed));

        Assert.Equal(HttpStatusCode.OK, technicians.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ticket.StatusCode);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
        var events = factory.LogSink.Events;
        Assert.Contains(events, logEvent => SourceOf(logEvent).StartsWith(AuthenticationNamespace, StringComparison.Ordinal));
        Assert.Empty(events
            .Where(logEvent => SourceOf(logEvent).StartsWith(DataProtectionNamespace, StringComparison.Ordinal))
            .Select(logEvent => $"{logEvent.Level} {SourceOf(logEvent)}: {logEvent.MessageTemplate.Text}"));
    }

    private static string SourceOf(LogEvent logEvent) => CapturingLogSink.Scalar(logEvent, "SourceContext") ?? string.Empty;

    private static bool IsDataProtection(Type type) =>
        type.Assembly.GetName().Name?.StartsWith(DataProtectionNamespace, StringComparison.Ordinal) == true;

    private static Type? ImplementationOf(ServiceDescriptor descriptor) => descriptor.IsKeyedService
        ? descriptor.KeyedImplementationType ?? descriptor.KeyedImplementationInstance?.GetType()
        : descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();

    private static IEnumerable<Type> TypesOf(ServiceDescriptor descriptor)
    {
        yield return descriptor.ServiceType;
        foreach (var argument in descriptor.ServiceType.GenericTypeArguments)
        {
            yield return argument;
        }

        if (ImplementationOf(descriptor) is { } implementation)
        {
            yield return implementation;
        }
    }

    private static bool Provides(ServiceDescriptor candidate, ServiceDescriptor expected)
    {
        if (candidate.ServiceType != expected.ServiceType || candidate.IsKeyedService != expected.IsKeyedService)
        {
            return false;
        }

        var implementation = ImplementationOf(expected);
        return implementation is not { IsVisible: true } || ImplementationOf(candidate) == implementation;
    }

    private static string Describe(ServiceDescriptor descriptor) =>
        $"{descriptor.ServiceType} -> {ImplementationOf(descriptor)?.ToString() ?? "factory"} ({descriptor.Lifetime})";
}
