using System;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Throws out of its own constructor while <see cref="FlakyConstructionGate.ShouldThrow" /> is set.</summary>
public sealed class FlakyConstructionActor : IActor
{
    public FlakyConstructionActor(FlakyConstructionGate gate) {
        if (gate.ShouldThrow) {
            throw new InvalidOperationException("construction failed");
        }
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