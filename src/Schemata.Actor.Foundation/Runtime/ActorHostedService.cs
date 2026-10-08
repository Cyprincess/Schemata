using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Runtime;

internal sealed class ActorHostedService(IActorSystem system, IHostApplicationLifetime lifetime) : IHostedService, IDisposable
{
    private CancellationTokenRegistration _stopping;
    private readonly CancellationTokenSource _collection = new();
    private Task? _collector;

    public Task StartAsync(CancellationToken cancellationToken) {
        if (system is InProcessActorSystem runtime) {
            _stopping = lifetime.ApplicationStopping.Register(() => runtime.BeginShutdown());
            _collector = runtime.CollectIdleAsync(_collection.Token);
        }
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken) {
        _collection.Cancel();
        Exception? failure = null;
        try {
            if (_collector is not null) await _collector;
        } catch (OperationCanceledException) when (_collection.IsCancellationRequested) { }
        catch (Exception error) { failure = error; }
        try { if (system is InProcessActorSystem runtime) await runtime.ShutdownAsync(cancellationToken); }
        catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public void Dispose() {
        _stopping.Dispose();
        _collection.Cancel();
        _collection.Dispose();
    }
}
