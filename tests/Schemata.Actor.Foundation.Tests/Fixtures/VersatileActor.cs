using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Answers <see cref="Ping" />, throws for <see cref="Fail" />, delays for <see cref="SlowPing" />, and deliberately never replies to <see cref="NoReply" />.</summary>
public sealed class VersatileActor : IActor
{
    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        switch (envelope.Payload) {
            case Ping ping:
                await ctx.ReplyAsync($"pong:{ping.Text}");
                break;
            case Fail fail:
                throw new InvalidOperationException(fail.Message);
            case SlowPing slow:
                await Task.Delay(slow.Delay, CancellationToken.None);
                await ctx.ReplyAsync("slow-done");
                break;
            case NoReply:
                // Deliberately does not call ReplyAsync, to exercise the "did not reply" fallback fault.
                break;
            case ReplyThenThrow rtt:
                // Records a reply, then throws in the same turn: the recorded reply must be
                // discarded and the Ask faulted with this exception instead (turn-end commit).
                await ctx.ReplyAsync(rtt.Reply);
                throw new InvalidOperationException(rtt.Message);
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);
}