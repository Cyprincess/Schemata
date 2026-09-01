using System;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Blocks its turn on a test-controlled <see cref="ManualGate" /> for <see cref="GateAndWait" />, so a test can deterministically keep a turn "still executing" for as long as it needs.</summary>
public sealed class GatedActor(ManualGate gate) : IActor
{
    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        switch (envelope.Payload) {
            case GateAndWait:
                await gate.WaitForReleaseAsync();
                await ctx.ReplyAsync("released");
                break;
            case Increment:
                await ctx.ReplyAsync(1);
                break;
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);
}