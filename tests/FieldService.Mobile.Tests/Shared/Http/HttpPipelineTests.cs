using System.Net;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Features.SignIn;
using FieldService.Mobile.Shared;
using FieldService.Mobile.Shared.Http;
using FieldService.Mobile.Shared.Session;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using NSubstitute;

namespace FieldService.Mobile.Tests.Shared.Http;

public sealed class HttpPipelineTests
{
    private const string FactoryLogPrefix = "System.Net.Http.HttpClient";

    [Fact]
    public async Task TheProductionJobsClientWritesOneResponseEntryWithoutTheQueryOrTheFactoryLogs()
    {
        var logs = new CapturingLoggerProvider();
        var stub = new StubHttpMessageHandler((_, _) => Task.FromResult(JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())));
        await using var provider = BuildProvider(stub, logs);

        var response = await provider.GetRequiredService<JobsApiClient>().GetAssignedJobsAsync(AssignedJobsView.Active, TestContext.Current.CancellationToken);

        Assert.Equal(3, response.Items.Count);
        var sent = Assert.Single(stub.Requests);
        Assert.Equal("/api/technicians/me/jobs", sent.Path);
        Assert.Equal("?view=active", sent.Query);
        var entry = Assert.Single(logs.Entries, entry => entry.EventName == "http.response");
        Assert.Equal(typeof(RequestLoggingHandler).FullName, entry.Category);
        Assert.Equal("GET", entry.Value("Method"));
        Assert.Equal(JobsApiClient.AssignedJobsRoute, entry.Value("RouteTemplate"));
        Assert.Equal("200", entry.Value("StatusCode"));
        Assert.NotNull(sent.CorrelationId);
        Assert.Equal(sent.CorrelationId, entry.Value("CorrelationId"));
        Assert.DoesNotContain(logs.Entries, captured => captured.Category.StartsWith(FactoryLogPrefix, StringComparison.Ordinal));
        Assert.DoesNotContain("view=", logs.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheProductionHandlersRunAuthorizationThenCorrelationIdThenRequestLogging()
    {
        var stub = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        await using var provider = BuildProvider(stub, new CapturingLoggerProvider());
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        var jobs = HandlerChain(factory.CreateHandler(nameof(JobsApiClient)));
        var auth = HandlerChain(factory.CreateHandler(nameof(AuthApiClient)));

        Assert.Equal(
            [typeof(AuthorizationHandler), typeof(CorrelationIdHandler), typeof(RequestLoggingHandler)],
            jobs.Delegating.Where(IsAppHandler));
        Assert.Equal(
            [typeof(CorrelationIdHandler), typeof(RequestLoggingHandler)],
            auth.Delegating.Where(IsAppHandler));
        Assert.DoesNotContain(jobs.Delegating, type => type.Namespace == "Microsoft.Extensions.Http.Logging");
        Assert.DoesNotContain(auth.Delegating, type => type.Namespace == "Microsoft.Extensions.Http.Logging");
        Assert.Same(stub, jobs.Primary);
        Assert.Same(stub, auth.Primary);
    }

    [Fact]
    public async Task OnlyTheJobsClientSendsTheBearerAndEveryRequestCarriesItsOwnCorrelationId()
    {
        var logs = new CapturingLoggerProvider();
        var accessToken = TestSession.NewAccessToken();
        var stub = new StubHttpMessageHandler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/api/auth/sign-in"
            ? ProblemResponses.Session(accessToken, TestSession.NewRefreshToken(), JobDtoBuilder.Marco, "Marco Reyes")
            : JobDtoBuilder.Json(JobDtoBuilder.MarcoActive())));
        await using var provider = BuildProvider(stub, logs);

        await provider.GetRequiredService<ISessionService>().SignInAsync("marco.reyes@fieldservice.example", TestSession.NewPassword(), TestContext.Current.CancellationToken);
        await provider.GetRequiredService<JobsApiClient>().GetAssignedJobsAsync(AssignedJobsView.Completed, TestContext.Current.CancellationToken);

        Assert.Equal(2, stub.Requests.Count);
        Assert.Null(stub.Requests[0].Authorization);
        Assert.Equal("Bearer " + accessToken, stub.Requests[1].Authorization);
        Assert.NotEqual(stub.Requests[0].CorrelationId, stub.Requests[1].CorrelationId);
        var responses = logs.Entries.Where(entry => entry.EventName == "http.response").ToList();
        Assert.Equal(["/api/auth/sign-in", JobsApiClient.AssignedJobsRoute], responses.Select(entry => entry.Value("RouteTemplate")));
        Assert.Equal(stub.Requests.Select(request => request.CorrelationId), responses.Select(entry => entry.Value("CorrelationId")));
        var text = logs.AllText();
        Assert.DoesNotContain(accessToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", text, StringComparison.Ordinal);
        Assert.DoesNotContain("view=", text, StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Entries, captured => captured.Category.StartsWith(FactoryLogPrefix, StringComparison.Ordinal));
    }

    private static bool IsAppHandler(Type type) => type.Namespace == typeof(AuthorizationHandler).Namespace;

    private static (IReadOnlyList<Type> Delegating, HttpMessageHandler? Primary) HandlerChain(HttpMessageHandler handler)
    {
        var chain = new List<Type>();
        HttpMessageHandler? current = handler;
        while (current is DelegatingHandler delegating)
        {
            chain.Add(delegating.GetType());
            current = delegating.InnerHandler;
        }

        return (chain, current);
    }

    private static ServiceProvider BuildProvider(HttpMessageHandler primary, CapturingLoggerProvider logs)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders().SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        services
            .AddSharedServices(TestSession.BaseUrl)
            .AddSessionFeature()
            .AddJobsFeature();
        services.AddSingleton(Substitute.For<ISecureStorage>());
        services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => primary));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = false, ValidateScopes = true });
    }
}
