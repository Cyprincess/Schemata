using System;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>
///     Observes whether the turn's own <see cref="IActorContext.Stopping" /> was canceled while a
///     <see cref="SlowPing" /> turn was still executing - proving an in-flight Ask's cancellation
///     reaches an already-executing handler rather than being silently ignored.
/// </summary>
public sealed class CancellationObservingActor : IActor
{
    public bool ObservedCancellation { get; private set; }

    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        switch (envelope.Payload) {
            case SlowPing slow:
                slow.Entered?.TrySetResult();
                try {
                    await Task.Delay(slow.Delay, ctx.Stopping);
                    await ctx.ReplyAsync("completed");
                } catch (OperationCanceledException) {
                    // The caller already gave up: record it and end the turn without replying,
                    // rather than escalating to supervision over a cancellation that was expected.
                    ObservedCancellation = true;
                }

                break;
            case GetObservedCancellation:
                await ctx.ReplyAsync(ObservedCancellation);
                break;
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);
}