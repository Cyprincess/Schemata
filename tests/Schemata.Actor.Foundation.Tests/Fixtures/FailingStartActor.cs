using System;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Extends <see cref="StartGateActor" /> with a failing initialization once released.</summary>
public sealed class FailingStartActor(LifecycleRecorder recorder, TaskCompletionSource<bool> startReleased) : IActor
{
    public async ValueTask OnStartedAsync(IActorContext ctx) {
        using (recorder.Enter("OnStarted")) {
            await startReleased.Task;
            throw new InvalidOperationException("startup failed");
        }
    }

    public ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        using (recorder.Enter("OnReceive")) { }
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) {
        using (recorder.Enter("OnStopped")) { }
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) {
        using (recorder.Enter("OnFailed")) { }
        return ValueTask.FromResult(false);
    }
}