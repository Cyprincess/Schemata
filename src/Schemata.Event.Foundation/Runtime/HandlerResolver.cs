using System;
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

    /// <summary>Invokes every registered event handler for <typeparamref name="TEvent"/> under the given <see cref="EventRouting"/>.</summary>
    public Task InvokeEventHandlersAsync<TEvent>(TEvent @event, EventRouting routing, CancellationToken ct)
        where TEvent : IEvent {
        var handlers = _services.GetServices<IEventHandler<TEvent>>().ToList();

        if (handlers.Count == 0) {
            var fallback = _services.GetServices<IEventHandler<IEvent>>().ToList();
            if (fallback.Count > 0) {
                handlers = fallback.Cast<IEventHandler<TEvent>>().ToList();
            }
        }

        if (handlers.Count == 0) {
            throw new InvalidOperationException($"No event handler registered for event type '{
                typeof(TEvent).FullName
            }'.");
        }

        if (routing == EventRouting.CompetingConsumers) {
            return handlers.First().HandleAsync(@event, ct);
        }

        var tasks = handlers.Select(h => h.HandleAsync(@event, ct));
        return Task.WhenAll(tasks);
    }

}
