using System;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Fails its own startup immediately and unconditionally - used to prove a synthesized <see cref="IActorContext.SpawnAsync" /> child never publishes a zombie dictionary entry.</summary>
public sealed class ImmediatelyFailingChildActor : IActor
{
    public ValueTask OnStartedAsync(IActorContext ctx) => throw new InvalidOperationException("child startup failed");

    public ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) => ValueTask.CompletedTask;

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(false);
}