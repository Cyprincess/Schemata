using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;
using Schemata.Event.Skeleton;
using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Event.Internal;

/// <summary>
///     Delivers every event of type <typeparamref name="TEvent" /> to the actors resolved by every
///     registered <see cref="IEventActorRoute{TEvent}" />, skipping routes that resolve
///     <see langword="null" />. The event instance itself is the mailbox payload -
///     <see cref="IEvent" /> already is <see cref="IMessage" /> - so no wrapper type is introduced.
///     With no route registered for <typeparamref name="TEvent" /> this type is never resolved and
///     nothing is delivered.
/// </summary>
public sealed class EventActorForwarder<TEvent>(
    IEnumerable<IEventActorRoute<TEvent>> routes,
    IActorSystem                          actors,
    IServiceProvider                      services
) : IEventHandler<TEvent>
    where TEvent : IEvent
{
    #region IEventHandler<TEvent> Members

    public async Task HandleAsync(TEvent @event, CancellationToken ct = default) {
        // Captured from this handler's own resolution scope, i.e. the event's consumption scope,
        // so ambient state restored on the actor's turn matches what was in effect when the event
        // was handled - not whatever happened to be ambient when it was originally published.
        var context = MessageContexts.Capture(services);

        foreach (var route in routes) {
            if (route.Resolve(@event) is not { } target) {
                continue;
            }

            var actor = await actors.GetAsync(target);
            await actor.TellAsync(@event, context, ct);
        }
    }

    #endregion
}
