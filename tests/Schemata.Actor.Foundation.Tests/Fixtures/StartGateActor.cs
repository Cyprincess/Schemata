using System;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>
///     Blocks <see cref="OnStartedAsync" /> on a test-controlled release source so a test can
///     deterministically hold initialization open, then record every lifecycle callback in order.
/// </summary>
public sealed class StartGateActor(LifecycleRecorder recorder, TaskCompletionSource<bool> startReleased) : IActor
{
    public async ValueTask OnStartedAsync(IActorContext ctx) {
        using (recorder.Enter("OnStarted")) {
            await startReleased.Task;
        }
    }

    public ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        using (recorder.Enter("OnReceive")) {
            if (envelope.CorrelationId != Guid.Empty) {
                _ = ctx.ReplyAsync("received");
            }
        }

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