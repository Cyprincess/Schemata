using System;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;
using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Event.Tests.Fixtures;

/// <summary>Remembers the last payload it received, answering <see cref="GetReceived" /> with it.</summary>
public sealed class RecordingActor : IActor
{
    private IMessage? _received;

    #region IActor Members

    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        switch (envelope.Payload) {
            case GetReceived:
                await ctx.ReplyAsync<IMessage?>(_received);
                break;
            default:
                _received = envelope.Payload;
                break;
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);

    #endregion
}