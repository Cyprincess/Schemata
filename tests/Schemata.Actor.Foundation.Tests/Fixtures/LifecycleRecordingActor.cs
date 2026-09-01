using System;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Records every lifecycle callback through a <see cref="LifecycleRecorder" />, blocking <see cref="GateAndWait" /> turns on a <see cref="ManualGate" /> so a test can hold one open on demand.</summary>
public sealed class LifecycleRecordingActor(LifecycleRecorder recorder, ManualGate gate) : IActor
{
    public ValueTask OnStartedAsync(IActorContext ctx) {
        using (recorder.Enter("OnStarted")) { }
        return ValueTask.CompletedTask;
    }

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        using (recorder.Enter("OnReceive")) {
            if (envelope.Payload is GateAndWait) {
                await gate.WaitForReleaseAsync();
                await ctx.ReplyAsync("released");
            }
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) {
        using (recorder.Enter("OnStopped")) { }
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) {
        using (recorder.Enter("OnFailed")) { }
        return ValueTask.FromResult(true);
    }
}