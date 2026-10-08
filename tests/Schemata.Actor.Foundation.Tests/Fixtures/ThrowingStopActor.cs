using System;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Throws from <see cref="IActor.OnStoppedAsync" /> so stop awaiters observe the failure.</summary>
public sealed class ThrowingStopActor : IActor
{
    public const string Message = "stop-callback-failed";

    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) => ValueTask.CompletedTask;

    public ValueTask OnStoppedAsync(IActorContext ctx) => throw new InvalidOperationException(Message);

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(false);
}
