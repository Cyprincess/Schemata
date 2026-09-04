using System;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Spawns an unregistered <see cref="ImmediatelyFailingChildActor" /> on <see cref="SpawnFailingChild" /> and replies with the child's own <see cref="IActorRef" />.</summary>
public sealed class SpawningParentActor : IActor
{
    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        if (envelope.Payload is SpawnFailingChild) {
            var child = await ctx.SpawnAsync(new(typeof(ImmediatelyFailingChildActor)));
            await ctx.ReplyAsync(child);
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);
}