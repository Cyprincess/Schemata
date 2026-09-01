using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Records the arrival order of every <see cref="Sequenced" /> message it receives.</summary>
public sealed class OrderRecordingActor : IActor
{
    private readonly List<int> _order = [];

    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        switch (envelope.Payload) {
            case Sequenced seq:
                _order.Add(seq.Index);
                await ctx.ReplyAsync(_order.Count);
                break;
            case GetOrder:
                await ctx.ReplyAsync((IReadOnlyList<int>)_order.ToArray());
                break;
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);
}