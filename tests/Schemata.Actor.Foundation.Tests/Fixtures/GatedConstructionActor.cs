using System;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Blocks its own construction on a test-controlled <see cref="ConstructionGate" />, so two <c>GetAsync</c> calls can be made to genuinely overlap while it is being built.</summary>
public sealed class GatedConstructionActor : IActor
{
    public GatedConstructionActor(ConstructionGate gate, SharedCounter counter) {
        gate.WaitForRelease();
        counter.Increment();
    }

    public Guid InstanceId { get; } = Guid.NewGuid();

    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope)
        => envelope.Payload switch {
            WhoAmI => ctx.ReplyAsync(InstanceId),
            _      => ValueTask.CompletedTask,
        };

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);
}