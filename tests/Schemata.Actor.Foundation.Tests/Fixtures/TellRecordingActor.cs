using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Records every <see cref="RecordTell" /> it receives, in arrival order.</summary>
public sealed class TellRecordingActor : IActor
{
    private readonly List<string> _received = [];

    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        switch (envelope.Payload) {
            case RecordTell record:
                _received.Add(record.Value);
                break;
            case GetReceived:
                await ctx.ReplyAsync((IReadOnlyList<string>)_received.ToArray());
                break;
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);
}