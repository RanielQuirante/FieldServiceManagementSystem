using FieldService.Api.Shared.Http;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Serilog.Events;

namespace FieldService.Api.Tests.Shared.Http;

[Collection(DatabaseCollection.Name)]
public sealed class TrustedProxyRuleTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string Key = ApiFactory.KnownProxiesKey;

    private const string MissingRule = "must list at least one proxy address outside Development and Testing";

    private const string AddressRule = "must be an IP address of one host";

    private const string ConfigInvalidEvent = "config.invalid";

    private const string HttpsOrigin = "https://fieldservice.example";

    private const int DistinctValueLength = 9;

    private static readonly string[] _allowedEventProperties = ["Key", "Rule", "EventId", "SourceContext"];

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task OutsideDevelopmentAndTestingTheHostRefusesToStartWithoutAKnownProxy(string environment)
    {
        var result = await StartAsync(environment, factory => factory.DefaultKnownProxies = []);

        AssertRefused(result, MissingRule, []);
    }

    [Theory]
    [InlineData("Production", "0.0.0.0")]
    [InlineData("Production", "::")]
    [InlineData("Production", "::ffff:0.0.0.0")]
    [InlineData("Staging", "255.255.255.255")]
    [InlineData("Staging", "239.192.83.17")]
    [InlineData("Production", "ff05::83:17")]
    [InlineData("Staging", "172.30.80")]
    [InlineData("Staging", "0254.30.80.10")]
    [InlineData("Production", "0xac.30.80.10")]
    [InlineData("Production", " ")]
    [InlineData("Development", "0.0.0.0")]
    [InlineData("Testing", "239.192.83.17")]
    public async Task AKnownProxyThatIsNotOneHostsAddressInStandardNotationStopsTheHost(string environment, string proxy)
    {
        var result = await StartAsync(environment, factory => factory.Settings[$"{Key}:0"] = proxy);

        AssertRefused(result, AddressRule, [proxy]);
    }

    [Theory]
    [InlineData("Production", "172.30.80.10")]
    [InlineData("Staging", "172.30.80.10")]
    [InlineData("Production", "2001:db8::10")]
    [InlineData("Production", "127.0.0.1")]
    public async Task OneHostAddressLetsTheHostStartOutsideDevelopmentAndTesting(string environment, string proxy)
    {
        var result = await StartAsync(environment, factory =>
        {
            factory.DefaultKnownProxies = [];
            factory.Settings[$"{Key}:0"] = proxy;
        });

        Assert.Null(result.Failure);
        Assert.Empty(result.ConfigInvalid);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task DevelopmentAndTestingStartWithoutAKnownProxyAndKeepTheLoopbackDefaults(string environment)
    {
        var result = await StartAsync(environment, factory => factory.DefaultKnownProxies = []);

        Assert.Null(result.Failure);
        Assert.Empty(result.ConfigInvalid);
    }

    [Theory]
    [InlineData("Production", "", MissingRule)]
    [InlineData("Staging", "", MissingRule)]
    [InlineData("staging", "", MissingRule)]
    [InlineData("Demo", "", MissingRule)]
    [InlineData("Production", "172.30.80.10;0.0.0.0", AddressRule)]
    [InlineData("Staging", "172.30.80.10;224.0.0.1", AddressRule)]
    [InlineData("Production", "172.30.80.10;172.30.80.11", null)]
    [InlineData("Production", "::ffff:172.30.80.10", null)]
    [InlineData("Development", "", null)]
    [InlineData("testing", "", null)]
    public void TheValidatorRequiresAHostAddressOutsideDevelopmentAndTesting(string environment, string proxies, string? expectedRule)
    {
        var options = new HttpOptions { AllowedHosts = "fieldservice.example" };
        options.ForwardedHeaders.KnownProxies = proxies.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var validator = new HttpOptionsValidator(
            new HostingEnvironment { EnvironmentName = environment },
            NullLogger<HttpOptionsValidator>.Instance);

        var result = validator.Validate(Options.DefaultName, options);

        var proxyFailures = (result.Failures ?? Enumerable.Empty<string>()).Where(message => message.StartsWith($"{Key}: ", StringComparison.Ordinal)).ToList();
        if (expectedRule is null)
        {
            Assert.Empty(proxyFailures);
            return;
        }

        var failure = Assert.Single(proxyFailures);
        Assert.Contains(expectedRule, failure, StringComparison.Ordinal);
        Assert.All(options.ForwardedHeaders.KnownProxies, proxy => Assert.DoesNotContain(proxy, failure, StringComparison.Ordinal));
    }

    private async Task<StartResult> StartAsync(string environment, Action<ApiFactory> configure)
    {
        var factory = Fixture.CreateFactory(factory =>
        {
            factory.EnvironmentName = environment;
            factory.Settings["Logs:FileEnabled"] = "false";
            if (environment == Environments.Production)
            {
                factory.Settings["Cors:AllowedOrigins:0"] = HttpsOrigin;
                factory.Settings["AllowedHosts"] = factory.ClientOptions.BaseAddress.Host;
            }

            configure(factory);
        });
        Exception? failure = null;
        try
        {
            using var client = factory.CreateClient();
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        await factory.DisposeAsync();
        return new StartResult(factory, failure);
    }

    private static void AssertRefused(StartResult result, string ruleFragment, IReadOnlyList<string> values)
    {
        Assert.True(result.Failure is not null, $"The host started although {Key} breaks a rule.");
        Assert.Contains(Flatten(result.Failure), exception =>
            exception is OptionsValidationException validation
            && validation.Failures.Any(message => message.StartsWith($"{Key}: ", StringComparison.Ordinal)
                && message.Contains(ruleFragment, StringComparison.Ordinal)));
        var logged = Assert.Single(result.ConfigInvalid);
        Assert.Equal(LogEventLevel.Fatal, logged.Level);
        Assert.Equal(Key, CapturingLogSink.Scalar(logged, "Key"));
        Assert.Contains(ruleFragment, CapturingLogSink.Scalar(logged, "Rule"), StringComparison.Ordinal);
        Assert.All(logged.Properties.Keys, name => Assert.Contains(name, _allowedEventProperties));

        var texts = Flatten(result.Failure)
            .Select(exception => $"exception {exception.GetType().Name}: {exception.Message}")
            .Concat(result.Events.Select(logEvent => $"log event {logEvent.MessageTemplate.Text}: {CapturingLogSink.Text(logEvent)}"))
            .ToList();
        var findings = values
            .Select(value => value.Trim())
            .Where(value => value.Length >= DistinctValueLength)
            .SelectMany(value => texts
                .Where(text => text.Contains(value, StringComparison.Ordinal))
                .Select(text => $"the value under test appears in {text.Split(':')[0]}"))
            .ToList();
        Assert.True(findings.Count == 0, string.Join("\n", findings));
    }

    private static IEnumerable<Exception> Flatten(Exception? exception)
    {
        if (exception is null)
        {
            yield break;
        }

        yield return exception;
        var inner = exception is AggregateException aggregate
            ? aggregate.InnerExceptions
            : exception.InnerException is { } single ? [single] : [];
        foreach (var child in inner.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    private sealed record StartResult(ApiFactory Factory, Exception? Failure)
    {
        public IReadOnlyList<LogEvent> Events { get; } = Factory.LogSink.Events;

        public IReadOnlyList<LogEvent> ConfigInvalid { get; } = Factory.LogSink.Named(ConfigInvalidEvent);
    }
}
