using System;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Increments a shared, externally observable counter every time one is actually constructed.</summary>
public sealed class CountingConstructionActor : IActor
{
    public CountingConstructionActor(SharedCounter counter) {
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