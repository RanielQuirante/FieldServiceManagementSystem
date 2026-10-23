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
        await harness.Driver.RunUntilAsync(() => harness.Logs.Named(RealtimeLog.DispatchedEvent).Count == 1, TimeSpan.FromSeconds(10));

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
        var service = new HangingService(harness);
        harness.Stub.Respond = service.RespondAsync;
        var jobEvent = JobEventFixtures.EventOf("job.assigned.v1.json");
        var start = harness.Clock.GetUtcNow();

        harness.Queue.Enqueue(jobEvent);
        await harness.Driver.RunUntilAsync(
            () => harness.Logs.Named(RealtimeLog.DispatchFailedEvent).Count > 0,
            TimeSpan.FromSeconds(15));

        var recorded = service.Attempts;
        Assert.Equal(service.Started, recorded.Count);
        Assert.InRange(recorded.Count, 2, 4);
        Assert.Equal(start, recorded[0].Started);
        Assert.Equal(TimeSpan.FromSeconds(10), harness.Clock.GetUtcNow() - start);
        Assert.All(recorded, attempt => Assert.True(attempt.Started - start < TimeSpan.FromSeconds(10)));
        Assert.All(recorded.SkipLast(1), attempt => Assert.Equal(TimeSpan.FromSeconds(2), attempt.Cancelled - attempt.Started));
        Assert.True(recorded[^1].Cancelled - recorded[^1].Started <= TimeSpan.FromSeconds(2));
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

        await harness.Driver.RunUntilAsync(() => harness.Logs.Named(RealtimeLog.DispatchFailedEvent).Count == 4, TimeSpan.FromSeconds(25));

        var failures = harness.Logs.Named(RealtimeLog.DispatchFailedEvent);
        Assert.Equal(events.Select(jobEvent => jobEvent.Id.ToString()), failures.Select(entry => CapturingLogSink.Scalar(entry, "JobEventId")));
        Assert.Equal(RealtimeLog.Unavailable, CapturingLogSink.Scalar(failures[0], "Reason"));
        Assert.All(failures.Skip(1), entry => Assert.Equal(RealtimeLog.CircuitOpen, CapturingLogSink.Scalar(entry, "Reason")));
        Assert.Equal(
            Enumerable.Repeat(events[0].Id.ToString(), 4),
            harness.Stub.Requests.Select(webhook => webhook.Header("webhook-id")));

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
        await harness.Driver.RunUntilAsync(() => stopping.IsCompleted, TimeSpan.FromSeconds(6));
        await stopping;

        Assert.Equal(JobEventDispatcher.DrainTimeout, harness.Clock.GetUtcNow() - stoppedAt);
        Assert.All(harness.Stub.Requests, webhook => Assert.Equal(events[0].Id.ToString(), webhook.Header("webhook-id")));
        var failure = Assert.Single(harness.Logs.Named(RealtimeLog.DispatchFailedEvent));
        Assert.Equal(RealtimeLog.ShuttingDown, CapturingLogSink.Scalar(failure, "Reason"));
        Assert.Equal(events[0].Id.ToString(), CapturingLogSink.Scalar(failure, "JobEventId"));
        var incomplete = Assert.Single(harness.Logs.Named(RealtimeLog.DrainIncompleteEvent));
        Assert.Equal(LogEventLevel.Warning, incomplete.Level);
        Assert.Equal("2", CapturingLogSink.Scalar(incomplete, "Remaining"));
    }

    [Fact]
    public async Task ASecondStopDuringTheDrainDoesNotExtendTheFiveSecondsThatTheFirstStopStarted()
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
        var firstStop = harness.Dispatcher.StopAsync(CancellationToken.None);
        await harness.Driver.RunToAsync(stoppedAt + TimeSpan.FromSeconds(3));
        var drainingAtSecondStop = !firstStop.IsCompleted;
        var secondStopAt = harness.Clock.GetUtcNow();
        var secondStop = harness.Dispatcher.StopAsync(CancellationToken.None);
        await harness.Driver.RunUntilAsync(() => firstStop.IsCompleted && secondStop.IsCompleted, TimeSpan.FromSeconds(6));
        await Task.WhenAll(firstStop, secondStop);

        Assert.True(drainingAtSecondStop);
        Assert.Equal(TimeSpan.FromSeconds(3), secondStopAt - stoppedAt);
        Assert.Equal(JobEventDispatcher.DrainTimeout, harness.Clock.GetUtcNow() - stoppedAt);
        Assert.All(harness.Stub.Requests, webhook => Assert.Equal(events[0].Id.ToString(), webhook.Header("webhook-id")));
        var failure = Assert.Single(harness.Logs.Named(RealtimeLog.DispatchFailedEvent));
        Assert.Equal(RealtimeLog.ShuttingDown, CapturingLogSink.Scalar(failure, "Reason"));
        Assert.Equal(events[0].Id.ToString(), CapturingLogSink.Scalar(failure, "JobEventId"));
        var incomplete = Assert.Single(harness.Logs.Named(RealtimeLog.DrainIncompleteEvent));
        Assert.Equal("2", CapturingLogSink.Scalar(incomplete, "Remaining"));
    }

    [Fact]
    public async Task StopAfterDisposeDoesNotThrowStillCompletesTheQueueAndSendsNothingFurther()
    {
        await using var harness = await DispatcherHarness.StartAsync();
        harness.Stub.Respond = NotificationsStub.Hang;
        var inFlight = JobEventFixtures.EventOf("job.created.v1.json");
        var queued = JobEventFixtures.EventOf("job.assigned.v1.json");
        harness.Queue.Enqueue(inFlight);
        harness.Queue.Enqueue(queued);
        await harness.Stub.WaitForRequestsAsync(1);

        harness.Dispatcher.Dispose();
        var stopFailure = await Record.ExceptionAsync(() => harness.Dispatcher
            .StopAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        var late = JobEventFixtures.NewEvent(9);
        harness.Queue.Enqueue(late);
        await harness.AdvanceUntilAsync(() => false, TimeSpan.FromSeconds(12), failWhenUnreached: false);

        Assert.Null(stopFailure);
        Assert.Equal([inFlight.Id.ToString()], harness.Stub.Requests.Select(webhook => webhook.Header("webhook-id")));
        Assert.Empty(harness.Logs.Named(RealtimeLog.DispatchedEvent));
        var cancelled = Assert.Single(
            harness.Logs.Named(RealtimeLog.DispatchFailedEvent),
            entry => CapturingLogSink.Scalar(entry, "JobEventId") == inFlight.Id.ToString());
        Assert.Equal(RealtimeLog.ShuttingDown, CapturingLogSink.Scalar(cancelled, "Reason"));
        var dropped = Assert.Single(harness.Logs.Named(RealtimeLog.DroppedEvent));
        Assert.Equal(late.Id.ToString(), CapturingLogSink.Scalar(dropped, "JobEventId"));
        Assert.Equal(RealtimeLog.QueueStopped, CapturingLogSink.Scalar(dropped, "Reason"));
    }

    [Fact]
    public async Task StopAndDisposeEachRunTwiceInTheInterleavedOrderOfTwoHostStopsWithoutThrowing()
    {
        await using var harness = await DispatcherHarness.StartAsync();
        var dispatcher = harness.Dispatcher;

        var failures = new[]
        {
            await Record.ExceptionAsync(() => dispatcher.StopAsync(CancellationToken.None)),
            Record.Exception(dispatcher.Dispose),
            await Record.ExceptionAsync(() => dispatcher.StopAsync(CancellationToken.None)),
            Record.Exception(dispatcher.Dispose),
        };

        Assert.All(failures, Assert.Null);
        var late = JobEventFixtures.NewEvent(9);
        harness.Queue.Enqueue(late);
        var dropped = Assert.Single(harness.Logs.Named(RealtimeLog.DroppedEvent));
        Assert.Equal(late.Id.ToString(), CapturingLogSink.Scalar(dropped, "JobEventId"));
        Assert.Empty(harness.Stub.Requests);
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

    private readonly ConcurrentQueue<StubExit> _exits = new();

    private int _entered;

    private int _holding;

    public Func<RecordedWebhook, CancellationToken, Task<HttpStatusCode>> Respond { get; set; } = Accept;

    public Func<long> TimerSequence { get; set; } = () => 0;

    public IReadOnlyList<RecordedWebhook> Requests => [.. _requests];

    public int Entered => Volatile.Read(ref _entered);

    public int Holding => Volatile.Read(ref _holding);

    public IReadOnlyList<StubExit> Exits => [.. _exits];

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
        Interlocked.Increment(ref _entered);
        var responding = Respond(webhook, cancellationToken);
        var holds = !responding.IsCompleted;
        if (holds)
        {
            Interlocked.Increment(ref _holding);
        }

        try
        {
            var status = await responding;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent("{\"outcome\":\"broadcast\"}", Encoding.UTF8, "application/json"),
                RequestMessage = request,
            };
        }
        finally
        {
            if (holds)
            {
                Interlocked.Decrement(ref _holding);
            }

            _exits.Enqueue(new StubExit(webhook.Header("webhook-id"), TimerSequence()));
        }
    }
}

internal sealed record StubExit(string? JobEventId, long TimerSequence);

internal sealed record HangingAttempt(DateTimeOffset Started, DateTimeOffset Cancelled);

internal sealed class HangingService(DispatcherHarness harness)
{
    private readonly ConcurrentQueue<DateTimeOffset> _started = new();

    private readonly ConcurrentQueue<HangingAttempt> _attempts = new();

    public int Started => _started.Count;

    public IReadOnlyList<HangingAttempt> Attempts => [.. _attempts];

    public async Task<HttpStatusCode> RespondAsync(RecordedWebhook webhook, CancellationToken cancellationToken)
    {
        var started = harness.Clock.GetUtcNow();
        _started.Enqueue(started);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return HttpStatusCode.Accepted;
        }
        catch (OperationCanceledException)
        {
            _attempts.Enqueue(new HangingAttempt(started, harness.Clock.GetUtcNow()));
            throw;
        }
    }
}

internal sealed class TimerDriver(FakeTimeProvider clock, TrackedClock timers, NotificationsStub stub, CapturingLogSink logs)
{
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan _poll = TimeSpan.FromMilliseconds(5);

    private static readonly string[] _endEvents = [RealtimeLog.DispatchedEvent, RealtimeLog.DispatchFailedEvent, RealtimeLog.DrainIncompleteEvent];

    public static TimerDriver AttachTo(ApiFactory factory, NotificationsStub stub)
    {
        var timers = new TrackedClock(factory.Clock);
        var previous = factory.TestServices;
        factory.TestServices = services =>
        {
            previous?.Invoke(services);
            services.AddSingleton<TimeProvider>(timers);
        };
        stub.TimerSequence = () => timers.Sequence;
        return new TimerDriver(factory.Clock, timers, stub, factory.LogSink);
    }

    public Task RunUntilAsync(Func<bool> done, TimeSpan limit) => RunAsync(done, clock.GetUtcNow() + limit, stopAtEnd: false);

    public Task RunToAsync(DateTimeOffset instant) => RunAsync(static () => false, instant, stopAtEnd: true);

    private async Task RunAsync(Func<bool> done, DateTimeOffset end, bool stopAtEnd)
    {
        while (true)
        {
            await WaitUntilAsync(() => done() || IsBlocked());
            if (done())
            {
                return;
            }

            var next = timers.NextDue;
            if (next is not { } due || due > end)
            {
                Assert.True(stopAtEnd, $"No timer is due by {end:O} and the condition is not met: {State()}");
                clock.Advance(end - clock.GetUtcNow());
                return;
            }

            var before = Progress();
            clock.Advance(due - clock.GetUtcNow());
            await WaitUntilAsync(() => done() || Progress() != before);
        }
    }

    private bool IsBlocked()
    {
        if (stub.Holding > 0)
        {
            return true;
        }

        var exits = stub.Exits;
        return exits.Count > 0
            && stub.Entered == exits.Count
            && !HasEnded(exits[^1].JobEventId)
            && timers.HasPendingAfter(exits[^1].TimerSequence);
    }

    private bool HasEnded(string? jobEventId) =>
        logs.Named(RealtimeLog.DispatchedEvent)
            .Concat(logs.Named(RealtimeLog.DispatchFailedEvent))
            .Any(entry => CapturingLogSink.Scalar(entry, "JobEventId") == jobEventId);

    private (int Entered, int Exited, int Ended) Progress() =>
        (stub.Entered, stub.Exits.Count, _endEvents.Sum(name => logs.Named(name).Count));

    private async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + _patience;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Nothing happened within {_patience} of real time: {State()}");
            await Task.Delay(_poll, TestContext.Current.CancellationToken);
        }
    }

    private string State() =>
        $"now {clock.GetUtcNow():O}, {stub.Entered} requests entered, {stub.Exits.Count} exited, {stub.Holding} holding, {Progress().Ended} dispatch ends logged, next timer {timers.NextDue:O}";
}

internal sealed class TrackedClock(FakeTimeProvider clock) : TimeProvider
{
    private readonly FakeTimeProvider _clock = clock;

    private readonly Lock _gate = new();

    private readonly Dictionary<TrackedTimer, (DateTimeOffset Due, long Sequence)> _pending = [];

    private long _sequence;

    public override TimeZoneInfo LocalTimeZone => _clock.LocalTimeZone;

    public override long TimestampFrequency => _clock.TimestampFrequency;

    public long Sequence
    {
        get
        {
            lock (_gate)
            {
                return _sequence;
            }
        }
    }

    public DateTimeOffset? NextDue
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count == 0 ? null : _pending.Values.Min(entry => entry.Due);
            }
        }
    }

    public override DateTimeOffset GetUtcNow() => _clock.GetUtcNow();

    public override long GetTimestamp() => _clock.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        new TrackedTimer(this, callback, state, dueTime, period);

    public bool HasPendingAfter(long sequence)
    {
        lock (_gate)
        {
            return _pending.Values.Any(entry => entry.Sequence > sequence);
        }
    }

    private void Schedule(TrackedTimer timer, TimeSpan dueTime)
    {
        lock (_gate)
        {
            if (dueTime == Timeout.InfiniteTimeSpan)
            {
                _pending.Remove(timer);
            }
            else
            {
                _pending[timer] = (_clock.GetUtcNow() + dueTime, ++_sequence);
            }
        }
    }

    private sealed class TrackedTimer : ITimer
    {
        private readonly TrackedClock _owner;

        private readonly TimerCallback _callback;

        private readonly object? _state;

        private readonly ITimer _inner;

        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public TrackedTimer(TrackedClock owner, TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
            _inner = owner._clock.CreateTimer(_ => Fire(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            Change(dueTime, period);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _period = period;
            _owner.Schedule(this, dueTime);
            return _inner.Change(dueTime, period);
        }

        public void Dispose()
        {
            _owner.Schedule(this, Timeout.InfiniteTimeSpan);
            _inner.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        private void Fire()
        {
            _owner.Schedule(this, _period == TimeSpan.Zero ? Timeout.InfiniteTimeSpan : _period);
            _callback(_state);
        }
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
        TrackedClock timers,
        CapturingLogSink logs,
        NotificationsStub stub,
        string secret)
    {
        _services = services;
        Clock = clock;
        Timers = timers;
        Logs = logs;
        Stub = stub;
        Secret = secret;
        SecretKey = Convert.FromBase64String(secret[SecretKeyRules.WebhookSecretPrefix.Length..]);
        Queue = services.GetRequiredService<JobEventQueue>();
        Dispatcher = services.GetServices<IHostedService>().OfType<JobEventDispatcher>().Single();
        stub.TimerSequence = () => timers.Sequence;
        Driver = new TimerDriver(clock, timers, stub, logs);
    }

    public FakeTimeProvider Clock { get; }

    public TrackedClock Timers { get; }

    public TimerDriver Driver { get; }

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
        var timers = new TrackedClock(clock);
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
        services.AddSingleton<TimeProvider>(timers);
        services.AddLogging(logging => logging
            .SetMinimumLevel(LogLevel.Trace)
            .AddSerilog(
                new LoggerConfiguration().MinimumLevel.Verbose().Enrich.FromLogContext().WriteTo.Sink(logs).CreateLogger(),
                dispose: true));
        services.AddRealtime(configuration);
        services.AddHttpClient<NotificationsClient>().ConfigurePrimaryHttpMessageHandler(() => stub);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var harness = new DispatcherHarness(provider, clock, timers, logs, stub, secret);
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
