using System;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Assigns a fresh identity on construction, so tests can tell "the same instance" apart from "a freshly (re)spawned one" across restart, stop and re-spawn.</summary>
public sealed class IdentityActor : IActor
{
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