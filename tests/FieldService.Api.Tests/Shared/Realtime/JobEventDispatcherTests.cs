using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Realtime;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Serilog;
using Serilog.Events;

namespace FieldService.Api.Tests.Shared.Realtime;

public sealed class JobEventDispatcherTests
{
    [Theory]
    [InlineData("job.created.v1.json")]
    [InlineData("job.assigned.v1.json")]
    [InlineData("job.status-changed.v1.json")]
    public void TheSerializerWritesEachInternalFixtureByteForByteFromItsOwnFieldValues(string fixture)
    {
        var jobEvent = JobEventFixtures.EventOf(fixture);

        var body = JobEventSerializer.Serialize(jobEvent);

        Assert.Equal(JobEventFixtures.Compact(fixture), body);
    }

    [Fact]
    public async Task EachEventIsPostedOnceInQueueOrderWithTheWebhookHeadersAndTheExactSerializedEnvelope()
    {
        await using var harness = await DispatcherHarness.StartAsync();

        foreach (var fixture in JobEventFixtures.InternalNames)
        {
            harness.Queue.Enqueue(JobEventFixtures.EventOf(fixture));
        }

        await harness.Stub.WaitForRequestsAsync(3);
        await harness.WaitForLogsAsync(RealtimeLog.DispatchedEvent, 3);

        Assert.Equal(3, harness.Stub.Requests.Count);
        var timestamp = harness.Clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        foreach (var (webhook, fixture) in harness.Stub.Requests.Zip(JobEventFixtures.InternalNames))
        {
            var expected = JobEventFixtures.EventOf(fixture);
            Assert.Equal("POST", webhook.Method);
            Assert.Equal(new Uri($"{NotificationsStub.BaseUrl}/internal/job-events"), webhook.Uri);
            Assert.Equal("application/json; charset=utf-8", webhook.ContentType);
            Assert.Equal(expected.Id.ToString(), webhook.Header("webhook-id"));
            Assert.Equal(timestamp, webhook.Header("webhook-timestamp"));
            Assert.Equal(expected.CorrelationId, webhook.Header("X-Correlation-ID"));
            Assert.Equal(JobEventFixtures.Compact(fixture), webhook.Body);
            Assert.Equal(JobEventFixtures.SignatureOf(harness.SecretKey, webhook), webhook.Header("webhook-signature"));
        }

        var dispatched = harness.Logs.Named(RealtimeLog.DispatchedEvent);
        Assert.All(dispatched, entry => Assert.Equal(LogEventLevel.Information, entry.Level));
        Assert.Equal(
            JobEventFixtures.InternalNames.Select(fixture => JobEventFixtures.EventOf(fixture).Id.ToString()),
            dispatched.Select(entry => CapturingLogSink.Scalar(entry, "JobEventId")));
        Assert.Equal(
            JobEventFixtures.InternalNames.Select(fixture => JobEventFixtures.EventOf(fixture).CorrelationId),
            dispatched.Select(entry => CapturingLogSink.Scalar(entry, "CorrelationId")));
        Assert.All(dispatched, entry => Assert.Equal("202", CapturingLogSink.Scalar(entry, "StatusCode")));
        Assert.Empty(harness.Logs.Named(RealtimeLog.DispatchFailedEvent));
        harness.AssertNoSecretMaterialLogged();
    }

    [Fact]
    public async Task ARetriedDeliveryResendsTheSameBytesAndHeadersAndEveryLogEntryCarriesTheCorrelationId()
    {
        await using var harness = await DispatcherHarness.StartAsync();
        var attempts = 0;
        harness.Stub.Respond = (_, _) => Task.FromResult(
            Interlocked.Increment(ref attempts) < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Accepted);
        var jobEvent = JobEventFixtures.EventOf("job.status-changed.v1.json");

        harness.Queue.Enqueue(jobEvent);
        await harness.AdvanceUntilAsync(() => harness.Logs.Named(RealtimeLog.DispatchedEvent).Count == 1, TimeSpan.FromSeconds(10));

        Assert.Equal(3, harness.Stub.Requests.Count);
        var first = harness.Stub.Requests[0];
        Assert.All(harness.Stub.Requests, webhook =>
        {
            Assert.Equal(first.Body, webhook.Body);
            Assert.Equal(first.Header("webhook-id"), webhook.Header("webhook-id"));
            Assert.Equal(first.Header("webhook-timestamp"), webhook.Header("webhook-timestamp"));
            Assert.Equal(first.Header("webhook-signature"), webhook.Header("webhook-signature"));
            Assert.Equal(first.Header("X-Correlation-ID"), webhook.Header("X-Correlation-ID"));
        });
        Assert.Empty(harness.Logs.Named(RealtimeLog.DispatchFailedEvent));
        var duringDispatch = harness.Logs.Events
            .Where(entry => CapturingLogSink.Scalar(entry, "SourceContext") is { } source
                && (source.StartsWith("Polly", StringComparison.Ordinal) || source.StartsWith("System.Net.Http", StringComparison.Ordinal)))
            .ToList();
        Assert.NotEmpty(duringDispatch);
        Assert.All(duringDispatch, entry => Assert.Equal(jobEvent.CorrelationId, CapturingLogSink.Scalar(entry, "CorrelationId")));
        harness.AssertNoSecretMaterialLogged();
    }

    [Fact]
    public async Task AHangingServiceGetsTwoSecondAttemptsNoAttemptAfterTheTenSecondBudgetAndThenOneWarning()
    {
        await using var harness = await DispatcherHarness.StartAsync();
        var attempts = new ConcurrentQueue<(DateTimeOffset Started, DateTimeOffset Cancelled)>();
        harness.Stub.Respond = async (_, cancellationToken) =>
        {
            var started = harness.Clock.GetUtcNow();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return HttpStatusCode.Accepted;
            }
            catch (OperationCanceledException)
            {
                attempts.Enqueue((started, harness.Clock.GetUtcNow()));
                throw;
            }
        };
        var jobEvent = JobEventFixtures.EventOf("job.assigned.v1.json");
        var start = harness.Clock.GetUtcNow();

        harness.Queue.Enqueue(jobEvent);
        await harness.AdvanceUntilAsync(() => harness.Logs.Named(RealtimeLog.DispatchFailedEvent).Count == 1, TimeSpan.FromSeconds(15));

        var elapsed = harness.Clock.GetUtcNow() - start;
        Assert.InRange(elapsed, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10.2));
        var recorded = attempts.ToList();
        Assert.InRange(recorded.Count, 2, 4);
        Assert.All(recorded, attempt => Assert.True(attempt.Started - start < TimeSpan.FromSeconds(10)));
        Assert.All(recorded.SkipLast(1), attempt =>
            Assert.InRange(attempt.Cancelled - attempt.Started, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2.1)));
        Assert.True(recorded[^1].Cancelled - recorded[^1].Started <= TimeSpan.FromSeconds(2.1));
        var failure = Assert.Single(harness.Logs.Named(RealtimeLog.DispatchFailedEvent));
        Assert.Equal(LogEventLevel.Warning, failure.Level);
        Assert.Equal(RealtimeLog.TimedOut, CapturingLogSink.Scalar(failure, "Reason"));
        Assert.Equal(jobEvent.Id.ToString(), CapturingLogSink.Scalar(failure, "JobEventId"));
        Assert.Equal(jobEvent.Type, CapturingLogSink.Scalar(failure, "Type"));
        Assert.Equal(jobEvent.JobId.ToString(), CapturingLogSink.Scalar(failure, "JobId"));
        Assert.Equal(jobEvent.CorrelationId, CapturingLogSink.Scalar(failure, "CorrelationId"));
        Assert.Empty(harness.Logs.Named(RealtimeLog.DispatchedEvent));
        harness.AssertNoSecretMaterialLogged();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(HttpStatusCode.UnsupportedMediaType)]
    public async Task APermanentRejectionIsNotRetriedLogsAWarningWithTheStatusAndTheNextEventIsSent(HttpStatusCode status)
    {
        await using var harness = await DispatcherHarness.StartAsync();
        var rejected = JobEventFixtures.EventOf("job.created.v1.json");
        var next = JobEventFixtures.EventOf("job.assigned.v1.json");
        harness.Stub.Respond = (webhook, _) => Task.FromResult(
            webhook.Header("webhook-id") == rejected.Id.ToString() ? status : HttpStatusCode.Accepted);

        harness.Queue.Enqueue(rejected);
        harness.Queue.Enqueue(next);
        await harness.WaitForLogsAsync(RealtimeLog.DispatchedEvent, 1);
        await harness.AdvanceUntilAsync(() => false, TimeSpan.FromSeconds(12), failWhenUnreached: false);

        Assert.Equal(
            [rejected.Id.ToString(), next.Id.ToString()],
            harness.Stub.Requests.Select(webhook => webhook.Header("webhook-id")));
        var failure = Assert.Single(harness.Logs.Named(RealtimeLog.DispatchFailedEvent));
        Assert.Equal(LogEventLevel.Warning, failure.Level);
        Assert.Equal(RealtimeLog.Rejected, CapturingLogSink.Scalar(failure, "Reason"));
        Assert.Equal(((int)status).ToString(CultureInfo.InvariantCulture), CapturingLogSink.Scalar(failure, "StatusCode"));
        Assert.Equal(rejected.Id.ToString(), CapturingLogSink.Scalar(failure, "JobEventId"));
        Assert.Equal(rejected.CorrelationId, CapturingLogSink.Scalar(failure, "CorrelationId"));
    }

    [Fact]
    public async Task TheCircuitBreakerOpensAfterRepeatedFailuresSoLaterEventsFailFastUntilItClosesAgain()
    {
        await using var harness = await DispatcherHarness.StartAsync();
        var available = false;
        harness.Stub.Respond = (_, _) => Task.FromResult(
            Volatile.Read(ref available) ? HttpStatusCode.Accepted : HttpStatusCode.ServiceUnavailable);
        var events = Enumerable.Range(1, 4).Select(number => JobEventFixtures.NewEvent(number)).ToList();

        foreach (var jobEvent in events)
        {
            harness.Queue.Enqueue(jobEvent);
        }

        await harness.AdvanceUntilAsync(() => harness.Logs.Named(RealtimeLog.DispatchFailedEvent).Count == 4, TimeSpan.FromSeconds(25));

        var failures = harness.Logs.Named(RealtimeLog.DispatchFailedEvent);
        Assert.Equal(events.Select(jobEvent => jobEvent.Id.ToString()), failures.Select(entry => CapturingLogSink.Scalar(entry, "JobEventId")));
        Assert.Contains(CapturingLogSink.Scalar(failures[0], "Reason"), new[] { RealtimeLog.Unavailable, RealtimeLog.TimedOut });
        Assert.All(failures.Skip(2), entry => Assert.Equal(RealtimeLog.CircuitOpen, CapturingLogSink.Scalar(entry, "Reason")));
        var attempted = harness.Stub.Requests.Select(webhook => webhook.Header("webhook-id")).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(events[2].Id.ToString(), attempted);
        Assert.DoesNotContain(events[3].Id.ToString(), attempted);
        Assert.InRange(harness.Stub.Requests.Count, 4, 5);

        Volatile.Write(ref available, true);
        harness.Clock.Advance(RealtimeRegistration.CircuitBreakDuration);
        var recovered = JobEventFixtures.NewEvent(5);
        harness.Queue.Enqueue(recovered);
        await harness.WaitForLogsAsync(RealtimeLog.DispatchedEvent, 1);

        var dispatched = Assert.Single(harness.Logs.Named(RealtimeLog.DispatchedEvent));
        Assert.Equal(recovered.Id.ToString(), CapturingLogSink.Scalar(dispatched, "JobEventId"));
    }

    [Fact]
    public async Task OnShutdownTheQueuedEventsAreStillDeliveredWithinTheDrainAndLaterEventsAreLoggedAsDropped()
    {
        await using var harness = await DispatcherHarness.StartAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Stub.Respond = async (_, cancellationToken) =>
        {
            await gate.Task.WaitAsync(cancellationToken);
            return HttpStatusCode.Accepted;
        };
        var events = JobEventFixtures.InternalNames.Select(JobEventFixtures.EventOf).ToList();
        foreach (var jobEvent in events)
        {
            harness.Queue.Enqueue(jobEvent);
        }

        await harness.Stub.WaitForRequestsAsync(1);
        var stopping = harness.Dispatcher.StopAsync(CancellationToken.None);
        var late = JobEventFixtures.NewEvent(9);
        harness.Queue.Enqueue(late);
        gate.SetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(
            events.Select(jobEvent => jobEvent.Id.ToString()),
            harness.Stub.Requests.Select(webhook => webhook.Header("webhook-id")));
        Assert.Equal(3, harness.Logs.Named(RealtimeLog.DispatchedEvent).Count);
        Assert.Empty(harness.Logs.Named(RealtimeLog.DrainIncompleteEvent));
        var dropped = Assert.Single(harness.Logs.Named(RealtimeLog.DroppedEvent));
        Assert.Equal(late.Id.ToString(), CapturingLogSink.Scalar(dropped, "JobEventId"));
        Assert.Equal(RealtimeLog.QueueStopped, CapturingLogSink.Scalar(dropped, "Reason"));
    }

    [Fact]
    public async Task TheDrainEndsAfterFiveSecondsAndLogsTheEventsItCouldNotDeliver()
    {
        await using var harness = await DispatcherHarness.StartAsync();
        harness.Stub.Respond = NotificationsStub.Hang;
        var events = JobEventFixtures.InternalNames.Select(JobEventFixtures.EventOf).ToList();
        foreach (var jobEvent in events)
        {
            harness.Queue.Enqueue(jobEvent);
        }

        await harness.Stub.WaitForRequestsAsync(1);
        var stoppedAt = harness.Clock.GetUtcNow();
        var stopping = harness.Dispatcher.StopAsync(CancellationToken.None);
        await harness.AdvanceUntilAsync(() => stopping.IsCompleted, TimeSpan.FromSeconds(6));
        await stopping;

        Assert.InRange(harness.Clock.GetUtcNow() - stoppedAt, JobEventDispatcher.DrainTimeout, JobEventDispatcher.DrainTimeout + TimeSpan.FromSeconds(0.2));
        Assert.All(harness.Stub.Requests, webhook => Assert.Equal(events[0].Id.ToString(), webhook.Header("webhook-id")));
        var failure = Assert.Single(harness.Logs.Named(RealtimeLog.DispatchFailedEvent));
        Assert.Equal(RealtimeLog.ShuttingDown, CapturingLogSink.Scalar(failure, "Reason"));
        Assert.Equal(events[0].Id.ToString(), CapturingLogSink.Scalar(failure, "JobEventId"));
        var incomplete = Assert.Single(harness.Logs.Named(RealtimeLog.DrainIncompleteEvent));
        Assert.Equal(LogEventLevel.Warning, incomplete.Level);
        Assert.Equal("2", CapturingLogSink.Scalar(incomplete, "Remaining"));
    }

    [Fact]
    public async Task TheClientUsesTwoSecondAttemptsThreeJitteredRetriesATenSecondBudgetAndACircuitBreaker()
    {
        await using var harness = await DispatcherHarness.StartAsync();

        var options = harness.Services
            .GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
            .Get($"{nameof(NotificationsClient)}-standard");

        Assert.Equal(TimeSpan.FromSeconds(2), options.AttemptTimeout.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(10), options.TotalRequestTimeout.Timeout);
        Assert.Equal(3, options.Retry.MaxRetryAttempts);
        Assert.True(options.Retry.UseJitter);
        Assert.Equal(TimeSpan.FromSeconds(1), options.Retry.Delay);
        Assert.Equal(4, options.CircuitBreaker.MinimumThroughput);
        Assert.Equal(0.5, options.CircuitBreaker.FailureRatio);
        Assert.Equal(TimeSpan.FromSeconds(30), options.CircuitBreaker.SamplingDuration);
        Assert.Equal(TimeSpan.FromSeconds(15), options.CircuitBreaker.BreakDuration);
    }

    [Fact]
    public async Task WithoutABaseUrlTheNullQueueIsUsedAndTheDispatcherSendsNothing()
    {
        await using var harness = await DispatcherHarness.StartAsync(baseUrl: string.Empty);

        var queue = harness.Services.GetRequiredService<IJobEventQueue>();
        queue.Enqueue(JobEventFixtures.EventOf("job.created.v1.json"));
        harness.Queue.Enqueue(JobEventFixtures.EventOf("job.assigned.v1.json"));
        await harness.AdvanceUntilAsync(() => false, TimeSpan.FromSeconds(1), failWhenUnreached: false);

        Assert.IsType<NullJobEventQueue>(queue);
        Assert.Empty(harness.Stub.Requests);
    }
}

internal sealed record RecordedWebhook(
    string Method,
    Uri Uri,
    IReadOnlyDictionary<string, string> Headers,
    string? ContentType,
    byte[] Body)
{
    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

internal sealed class NotificationsStub : HttpMessageHandler
{
    public const string BaseUrl = "http://notifications.test:5090";

    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);

    private readonly ConcurrentQueue<RecordedWebhook> _requests = new();

    public Func<RecordedWebhook, CancellationToken, Task<HttpStatusCode>> Respond { get; set; } = Accept;

    public IReadOnlyList<RecordedWebhook> Requests => [.. _requests];

    public static NotificationsStub AttachTo(ApiFactory factory)
    {
        var stub = new NotificationsStub();
        factory.Settings[NotificationsOptions.BaseUrlName] = BaseUrl;
        var previous = factory.TestServices;
        factory.TestServices = services =>
        {
            previous?.Invoke(services);
            services.RemoveAll<IJobEventQueue>();
            services.AddSingleton<IJobEventQueue>(provider => provider.GetRequiredService<JobEventQueue>());
            services.AddHttpClient<NotificationsClient>().ConfigurePrimaryHttpMessageHandler(() => stub);
        };
        return stub;
    }

    public static byte[] SecretKeyOf(ApiFactory factory) =>
        Convert.FromBase64String(factory.Settings[NotificationsOptions.WebhookSecretName]![SecretKeyRules.WebhookSecretPrefix.Length..]);

    public static void AssertSignedEnvelope(
        ApiFactory factory,
        RecordedWebhook webhook,
        string correlationId,
        string type,
        Guid jobId,
        long jobNumber,
        JobStatus status,
        JobStatus? previousStatus)
    {
        var now = factory.Clock.GetUtcNow();
        Assert.Equal("POST", webhook.Method);
        Assert.Equal(new Uri($"{BaseUrl}/internal/job-events"), webhook.Uri);
        Assert.Equal("application/json; charset=utf-8", webhook.ContentType);
        Assert.Equal(correlationId, webhook.Header("X-Correlation-ID"));
        Assert.Equal(now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), webhook.Header("webhook-timestamp"));
        Assert.Equal(JobEventFixtures.SignatureOf(SecretKeyOf(factory), webhook), webhook.Header("webhook-signature"));
        using var envelope = JsonDocument.Parse(webhook.Body);
        var root = envelope.RootElement;
        Assert.Equal(["id", "type", "version", "occurredAt", "correlationId", "data"], root.EnumerateObject().Select(member => member.Name));
        Assert.Equal(webhook.Header("webhook-id"), root.GetProperty("id").GetString());
        Assert.Equal(7, root.GetProperty("id").GetGuid().Version);
        Assert.Equal(type, root.GetProperty("type").GetString());
        Assert.Equal(JobEvent.CurrentVersion, root.GetProperty("version").GetInt32());
        Assert.Equal(ApiClient.WireInstant(UtcInstant.Truncate(now)), root.GetProperty("occurredAt").GetString());
        Assert.Equal(correlationId, root.GetProperty("correlationId").GetString());
        var data = root.GetProperty("data");
        Assert.Equal(["jobId", "jobNumber", "status", "previousStatus"], data.EnumerateObject().Select(member => member.Name));
        Assert.Equal(jobId, data.GetProperty("jobId").GetGuid());
        Assert.Equal(jobNumber, data.GetProperty("jobNumber").GetInt64());
        Assert.Equal(status.ToString(), data.GetProperty("status").GetString());
        Assert.Equal(previousStatus?.ToString(), data.GetProperty("previousStatus").GetString());
    }

    public static Task<HttpStatusCode> Accept(RecordedWebhook webhook, CancellationToken cancellationToken) =>
        Task.FromResult(HttpStatusCode.Accepted);

    public static async Task<HttpStatusCode> Hang(RecordedWebhook webhook, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return HttpStatusCode.Accepted;
    }

    public async Task WaitForRequestsAsync(int count)
    {
        var deadline = DateTime.UtcNow + _patience;
        while (_requests.Count < count)
        {
            Assert.True(DateTime.UtcNow < deadline, $"The notification service stub received {_requests.Count} of {count} requests.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var headers = request.Headers
            .Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase);
        var webhook = new RecordedWebhook(
            request.Method.Method,
            request.RequestUri!,
            headers,
            request.Content?.Headers.ContentType?.ToString(),
            body);
        _requests.Enqueue(webhook);
        var status = await Respond(webhook, cancellationToken);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent("{\"outcome\":\"broadcast\"}", Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };
    }
}

internal static class JobEventFixtures
{
    public static readonly string[] InternalNames = ["job.created.v1.json", "job.assigned.v1.json", "job.status-changed.v1.json"];

    public static byte[] Compact(string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(PathOf(name)));
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            document.WriteTo(writer);
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static JobEvent EventOf(string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(PathOf(name)));
        var root = document.RootElement;
        var data = root.GetProperty("data");
        var previous = data.GetProperty("previousStatus");
        return new JobEvent(
            root.GetProperty("id").GetGuid(),
            root.GetProperty("type").GetString()!,
            root.GetProperty("version").GetInt32(),
            DateTimeOffset.Parse(root.GetProperty("occurredAt").GetString()!, CultureInfo.InvariantCulture),
            root.GetProperty("correlationId").GetString()!,
            data.GetProperty("jobId").GetGuid(),
            data.GetProperty("jobNumber").GetInt64(),
            Enum.Parse<JobStatus>(data.GetProperty("status").GetString()!),
            previous.ValueKind == JsonValueKind.Null ? null : Enum.Parse<JobStatus>(previous.GetString()!));
    }

    public static JobEvent NewEvent(int number)
    {
        var occurredAt = new DateTimeOffset(2026, 10, 19, 14, 42, 0, TimeSpan.Zero).AddSeconds(number);
        return new JobEvent(
            Guid.CreateVersion7(occurredAt),
            JobEventTypes.StatusChanged,
            JobEvent.CurrentVersion,
            occurredAt,
            $"dispatch-{number}",
            Guid.CreateVersion7(occurredAt),
            1000 + number,
            JobStatus.InProgress,
            JobStatus.Assigned);
    }

    public static string SignatureOf(byte[] key, RecordedWebhook webhook)
    {
        var prefix = Encoding.UTF8.GetBytes($"{webhook.Header("webhook-id")}.{webhook.Header("webhook-timestamp")}.");
        return $"v1,{Convert.ToBase64String(HMACSHA256.HashData(key, prefix.Concat(webhook.Body).ToArray()))}";
    }

    private static string PathOf(string name) => Path.Combine(RepositoryPaths.EventContractsDirectory, "internal", name);
}

internal sealed class RealtimeLogs : IDisposable
{
    public RealtimeLogs()
    {
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.Sink(Sink)
            .CreateLogger();
        Factory = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Trace).AddSerilog(serilog, dispose: true));
    }

    public CapturingLogSink Sink { get; } = new();

    public ILoggerFactory Factory { get; }

    public void Dispose() => Factory.Dispose();
}

internal sealed class DispatcherHarness : IAsyncDisposable
{
    private static readonly TimeSpan _step = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan _settle = TimeSpan.FromMilliseconds(10);

    private readonly ServiceProvider _services;

    private DispatcherHarness(
        ServiceProvider services,
        FakeTimeProvider clock,
        CapturingLogSink logs,
        NotificationsStub stub,
        string secret)
    {
        _services = services;
        Clock = clock;
        Logs = logs;
        Stub = stub;
        Secret = secret;
        SecretKey = Convert.FromBase64String(secret[SecretKeyRules.WebhookSecretPrefix.Length..]);
        Queue = services.GetRequiredService<JobEventQueue>();
        Dispatcher = services.GetServices<IHostedService>().OfType<JobEventDispatcher>().Single();
    }

    public FakeTimeProvider Clock { get; }

    public CapturingLogSink Logs { get; }

    public NotificationsStub Stub { get; }

    public JobEventQueue Queue { get; }

    public JobEventDispatcher Dispatcher { get; }

    public IServiceProvider Services => _services;

    public string Secret { get; }

    public byte[] SecretKey { get; }

    public static async Task<DispatcherHarness> StartAsync(string baseUrl = NotificationsStub.BaseUrl)
    {
        var secret = SecretKeyRules.WebhookSecretPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 19, 14, 42, 0, TimeSpan.Zero));
        var logs = new CapturingLogSink();
        var stub = new NotificationsStub();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [NotificationsOptions.BaseUrlName] = baseUrl,
                [NotificationsOptions.WebhookSecretName] = secret,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = ApiFactory.TestingEnvironment });
        services.AddSingleton<TimeProvider>(clock);
        services.AddLogging(logging => logging
            .SetMinimumLevel(LogLevel.Trace)
            .AddSerilog(
                new LoggerConfiguration().MinimumLevel.Verbose().Enrich.FromLogContext().WriteTo.Sink(logs).CreateLogger(),
                dispose: true));
        services.AddRealtime(configuration);
        services.AddHttpClient<NotificationsClient>().ConfigurePrimaryHttpMessageHandler(() => stub);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var harness = new DispatcherHarness(provider, clock, logs, stub, secret);
        await harness.Dispatcher.StartAsync(TestContext.Current.CancellationToken);
        return harness;
    }

    public Task AdvanceUntilAsync(Func<bool> done, TimeSpan limit, bool failWhenUnreached = true) =>
        AdvanceUntilAsync(Clock, done, limit, failWhenUnreached);

    public Task WaitForLogsAsync(string eventName, int count) => WaitForLogsAsync(Logs, eventName, count);

    public static async Task AdvanceUntilAsync(FakeTimeProvider clock, Func<bool> done, TimeSpan limit, bool failWhenUnreached = true)
    {
        var advanced = TimeSpan.Zero;
        while (true)
        {
            await Task.Delay(_settle, TestContext.Current.CancellationToken);
            if (done())
            {
                return;
            }

            if (advanced >= limit)
            {
                Assert.False(failWhenUnreached, $"The condition was not met within {limit} of fake time.");
                return;
            }

            clock.Advance(_step);
            advanced += _step;
        }
    }

    public static async Task WaitForLogsAsync(CapturingLogSink logs, string eventName, int count)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (logs.Named(eventName).Count < count)
        {
            Assert.True(DateTime.UtcNow < deadline, $"{eventName} was logged {logs.Named(eventName).Count} of {count} times.");
            await Task.Delay(_settle, TestContext.Current.CancellationToken);
        }
    }

    public void AssertNoSecretMaterialLogged()
    {
        var forbidden = new List<string> { Secret, Secret[SecretKeyRules.WebhookSecretPrefix.Length..] };
        foreach (var webhook in Stub.Requests)
        {
            forbidden.Add(webhook.Header("webhook-signature")!);
            forbidden.Add(webhook.Header("webhook-signature")!["v1,".Length..]);
            forbidden.Add(Encoding.UTF8.GetString(webhook.Body));
        }

        var leaks = Logs.Events
            .Select(CapturingLogSink.Text)
            .Where(text => forbidden.Any(value => text.Contains(value, StringComparison.Ordinal)))
            .ToList();
        Assert.True(leaks.Count == 0, "Log entries hold a secret, a signature or a body:\n" + string.Join("\n", leaks));
    }

    public async ValueTask DisposeAsync()
    {
        var stopping = Dispatcher.StopAsync(CancellationToken.None);
        await AdvanceUntilAsync(() => stopping.IsCompleted, TimeSpan.FromSeconds(10), failWhenUnreached: false);
        await stopping.WaitAsync(TimeSpan.FromSeconds(10));
        await _services.DisposeAsync();
    }
}
