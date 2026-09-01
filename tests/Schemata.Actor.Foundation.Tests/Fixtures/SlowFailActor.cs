using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Delays, then throws, and always stops (never restarts) - used to prove queued Asks behind a failing turn are drained and faulted rather than left hanging.</summary>
public sealed class SlowFailActor : IActor
{
    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        switch (envelope.Payload) {
            case SlowFail slow:
                await Task.Delay(slow.Delay, CancellationToken.None);
                throw new InvalidOperationException(slow.Message);
            case Increment:
                await ctx.ReplyAsync(1);
                break;
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(false);
}