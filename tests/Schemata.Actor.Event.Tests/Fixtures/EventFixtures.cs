using System;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;
using Schemata.Event.Skeleton;
using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Event.Tests.Fixtures;

#region Events

public sealed record OrderPlaced(string OrderId) : IEvent;

public sealed record OrderCancelled(string OrderId) : IEvent;

#endregion

#region Messages

public sealed record GetReceived : IRequest<IMessage?>;

#endregion

#region Routes

/// <summary>Routes every <see cref="OrderPlaced" /> to the recorder actor keyed by its order id.</summary>
public sealed class OrderPlacedRoute : IEventActorRoute<OrderPlaced>
{
    public ActorId? Resolve(OrderPlaced @event) => new("recorder", @event.OrderId);
}

#endregion

#region Actors

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

#endregion
