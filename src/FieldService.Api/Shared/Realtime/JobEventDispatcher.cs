using System.Net;
using FieldService.Api.Shared.Observability;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Serilog.Context;

namespace FieldService.Api.Shared.Realtime;

public sealed class JobEventDispatcher(
    JobEventQueue queue,
    IOptions<NotificationsOptions> options,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<JobEventDispatcher> logger) : BackgroundService
{
    public static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    private static readonly HashSet<HttpStatusCode> _transientStatuses =
    [
        HttpStatusCode.RequestTimeout,
        HttpStatusCode.TooManyRequests,
    ];

    private readonly CancellationTokenSource _drainDeadline = new(Timeout.InfiniteTimeSpan, time);

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Complete();
        _drainDeadline.CancelAfter(DrainTimeout);
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _drainDeadline.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IsEnabled)
        {
            return;
        }

        var signer = WebhookSigner.FromSecret(options.Value.WebhookSecret);
        var reader = queue.Reader;
        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                while (!stoppingToken.IsCancellationRequested && reader.TryRead(out var jobEvent))
                {
                    await DispatchAsync(jobEvent, signer, _drainDeadline.Token);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        while (!_drainDeadline.IsCancellationRequested && reader.TryRead(out var remaining))
        {
            await DispatchAsync(remaining, signer, _drainDeadline.Token);
        }

        if (reader.Count > 0)
        {
            RealtimeLog.DrainIncomplete(logger, reader.Count);
        }
    }

    private async Task DispatchAsync(JobEvent jobEvent, WebhookSigner signer, CancellationToken cancellationToken)
    {
        using var correlation = LogContext.PushProperty(RequestLoggingSetup.CorrelationIdProperty, jobEvent.CorrelationId);
        try
        {
            var body = JobEventSerializer.Serialize(jobEvent);
            var timestamp = time.GetUtcNow().ToUnixTimeSeconds();
            var signature = signer.Sign(jobEvent.Id.ToString(), timestamp, body);
            await using var scope = scopes.CreateAsyncScope();
            var client = scope.ServiceProvider.GetRequiredService<NotificationsClient>();
            var status = await client.SendAsync(jobEvent, body, timestamp, signature, cancellationToken);
            if ((int)status is >= 200 and < 300)
            {
                RealtimeLog.Dispatched(logger, jobEvent.Id, jobEvent.Type, jobEvent.JobId, jobEvent.CorrelationId, (int)status);
                return;
            }

            LogFailure(jobEvent, IsTransient(status) ? RealtimeLog.Unavailable : RealtimeLog.Rejected, (int)status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogFailure(jobEvent, RealtimeLog.ShuttingDown, null);
        }
        catch (Exception exception)
        {
            LogFailure(jobEvent, ReasonOf(exception), null);
        }
    }

    private void LogFailure(JobEvent jobEvent, string reason, int? statusCode) =>
        RealtimeLog.DispatchFailed(
            logger,
            jobEvent.Id,
            jobEvent.Type,
            jobEvent.JobId,
            jobEvent.CorrelationId,
            reason,
            statusCode);

    private static bool IsTransient(HttpStatusCode status) =>
        (int)status >= 500 || _transientStatuses.Contains(status);

    private static string ReasonOf(Exception exception) => exception switch
    {
        TimeoutRejectedException => RealtimeLog.TimedOut,
        BrokenCircuitException => RealtimeLog.CircuitOpen,
        HttpRequestException => RealtimeLog.Unreachable,
        _ => RealtimeLog.Failed,
    };
}
