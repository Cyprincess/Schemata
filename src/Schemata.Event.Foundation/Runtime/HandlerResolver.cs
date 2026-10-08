using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Event.Skeleton;

namespace Schemata.Event.Foundation.Runtime;

/// <summary>Resolves and invokes <see cref="IEventHandler{TEvent}"/> instances from DI.</summary>
public sealed class HandlerResolver
{
    private readonly IServiceProvider _services;

    /// <summary>Initializes a resolver over the supplied service provider.</summary>
    public HandlerResolver(IServiceProvider services) { _services = services; }

    /// <summary>
    ///     Resolves the candidate set for one dispatch: every typed <see cref="IEventHandler{TEvent}"/>
    ///     candidate first (in its own DI registration order), then every catch-all
    ///     <see cref="IEventHandler{IEvent}"/> subscriber (in its own registration order). Call it
    ///     once per dispatch and hand the result to
    ///     <see cref="InvokeEventHandlersAsync{TEvent}(TEvent, IReadOnlyList{IEventHandler{TEvent}}, EventRouting, CancellationToken)"/>,
    ///     so probing and invocation share one set and transient handlers are constructed once.
    /// </summary>
    /// <remarks>
    ///     One object registered under both contracts is deduplicated by instance identity; two
    ///     distinct instances of the same implementation type stay separate candidates.
    /// </remarks>
    public IReadOnlyList<IEventHandler<TEvent>> ResolveHandlers<TEvent>()
        where TEvent : IEvent {
        var handlers = _services.GetServices<IEventHandler<TEvent>>().ToList();
        if (typeof(TEvent) == typeof(IEvent)) {
            return handlers;
        }

        // IEventHandler<in TEvent> contravariance makes every catch-all subscriber a typed handler.
        foreach (var subscriber in _services.GetServices<IEventHandler<IEvent>>()) {
            var candidate = (IEventHandler<TEvent>)subscriber;
            if (handlers.Any(handler => ReferenceEquals(handler, candidate))) {
                continue;
            }

            handlers.Add(candidate);
        }

        return handlers;
    }

    /// <summary>
    ///     Resolves the <see cref="ResolveHandlers{TEvent}"/> candidate set and invokes it under
    ///     <paramref name="routing"/>. An empty set is a consume-side configuration error and throws
    ///     <see cref="InvalidOperationException"/>.
    /// </summary>
    public Task InvokeEventHandlersAsync<TEvent>(TEvent @event, EventRouting routing, CancellationToken ct)
        where TEvent : IEvent {
        return InvokeEventHandlersAsync(@event, ResolveHandlers<TEvent>(), routing, ct);
    }

    /// <summary>
    ///     Invokes an already-resolved candidate set under <paramref name="routing"/>: broadcast awaits
    ///     every candidate, competing consumers invoke exactly one. An empty set throws
    ///     <see cref="InvalidOperationException"/>.
    /// </summary>
    public Task InvokeEventHandlersAsync<TEvent>(
        TEvent                               @event,
        IReadOnlyList<IEventHandler<TEvent>> handlers,
        EventRouting                         routing,
        CancellationToken                    ct
    )
        where TEvent : IEvent {
        if (handlers.Count == 0) {
            throw new InvalidOperationException($"No event handler registered for event type '{
                typeof(TEvent).FullName
            }'.");
        }

        if (routing == EventRouting.CompetingConsumers) {
            return handlers[0].HandleAsync(@event, ct);
        }

        var tasks = handlers.Select(handler => handler.HandleAsync(@event, ct));
        return Task.WhenAll(tasks);
    }
}
