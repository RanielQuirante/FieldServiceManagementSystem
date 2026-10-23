using System.Collections.Concurrent;
using System.Net;
using FieldService.Api.Shared.Health;
using FieldService.Api.Shared.Realtime;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FieldService.Api.Tests.Shared.Realtime;

[Collection(DatabaseCollection.Name)]
public sealed class JobEventDispatcherShutdownTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task TheHostShutsDownWithoutErrorsWhenOneOfItsTwoStopsReachesTheDispatcherAfterTheOtherDisposedTheHost()
    {
        var probe = new ShutdownProbe();
        var factory = Fixture.CreateFactory(created => created.TestServices = probe.Install);
        Exception? disposeFailure;
        try
        {
            using var client = factory.CreateClient();
            using var live = await client.GetAsync(HealthRegistration.LivePath, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        }
        finally
        {
            disposeFailure = await Record.ExceptionAsync(() => factory.DisposeAsync().AsTask().WaitAsync(_patience, CancellationToken));
        }

        await probe.BothStopsFinished.WaitAsync(_patience, CancellationToken);

        Assert.Null(disposeFailure);
        Assert.Empty(probe.Failures);
        Assert.Equal([ShutdownProbe.StopBeforeDispose, ShutdownProbe.Disposed, ShutdownProbe.StopAfterDispose], probe.Calls);
    }
}

internal sealed class ShutdownProbe
{
    public const string StopBeforeDispose = "stop before dispose";

    public const string Disposed = "dispose";

    public const string StopAfterDispose = "stop after dispose";

    private static readonly TimeSpan _gateLimit = TimeSpan.FromSeconds(15);

    private readonly ConcurrentQueue<string> _calls = new();

    private readonly ConcurrentQueue<Exception> _failures = new();

    private readonly TaskCompletionSource _dispatcherDisposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource _bothStopsFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _gateCalls;

    private int _stopsFinished;

    public IReadOnlyList<string> Calls => [.. _calls];

    public IReadOnlyList<Exception> Failures => [.. _failures];

    public Task BothStopsFinished => _bothStopsFinished.Task;

    public void Install(IServiceCollection services)
    {
        var registration = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(JobEventDispatcher));
        services.Remove(registration);
        services.AddSingleton<IHostedService>(provider =>
            new ObservedDispatcher(ActivatorUtilities.CreateInstance<JobEventDispatcher>(provider), this));
        services.AddSingleton<IHostedService>(new StopGate(this));
    }

    private async Task HoldFirstStopUntilTheDispatcherIsDisposedAsync()
    {
        if (Interlocked.Increment(ref _gateCalls) == 1)
        {
            await _dispatcherDisposed.Task.WaitAsync(_gateLimit).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private async Task ObserveStopAsync(Func<Task> stop)
    {
        _calls.Enqueue(_dispatcherDisposed.Task.IsCompleted ? StopAfterDispose : StopBeforeDispose);
        try
        {
            await stop();
        }
        catch (Exception exception)
        {
            _failures.Enqueue(exception);
            throw;
        }
        finally
        {
            if (Interlocked.Increment(ref _stopsFinished) == 2)
            {
                _bothStopsFinished.TrySetResult();
            }
        }
    }

    private void ObserveDispose(Action dispose)
    {
        _calls.Enqueue(Disposed);
        try
        {
            dispose();
        }
        catch (Exception exception)
        {
            _failures.Enqueue(exception);
            throw;
        }
        finally
        {
            _dispatcherDisposed.TrySetResult();
        }
    }

    private sealed class ObservedDispatcher(JobEventDispatcher dispatcher, ShutdownProbe probe) : IHostedService, IDisposable
    {
        public Task StartAsync(CancellationToken cancellationToken) => dispatcher.StartAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) =>
            probe.ObserveStopAsync(() => dispatcher.StopAsync(cancellationToken));

        public void Dispose() => probe.ObserveDispose(dispatcher.Dispose);
    }

    private sealed class StopGate(ShutdownProbe probe) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => probe.HoldFirstStopUntilTheDispatcherIsDisposedAsync();
    }
}
