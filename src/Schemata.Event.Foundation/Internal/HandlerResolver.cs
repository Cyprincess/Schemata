using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Event.Skeleton;

namespace Schemata.Event.Foundation.Internal;

/// <summary>Resolves and invokes <see cref="IEventHandler{TEvent}"/> and <see cref="IRequestHandler{TRequest, TResponse}"/> instances from DI.</summary>
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

    /// <summary>Invokes the single registered request handler for <typeparamref name="TRequest"/>.</summary>
    public Task<TResponse> InvokeRequestHandlerAsync<TRequest, TResponse>(TRequest request, CancellationToken ct)
        where TRequest : IRequest<TResponse> {
        var handlers = _services.GetServices<IRequestHandler<TRequest, TResponse>>().ToList();

        if (handlers.Count == 0) {
            throw new InvalidOperationException($"No request handler registered for request type '{
                typeof(TRequest).FullName
            }'.");
        }

        if (handlers.Count > 1) {
            throw new InvalidOperationException($"Multiple request handlers registered for request type '{
                typeof(TRequest).FullName
            }'. Expected exactly one.");
        }

        return handlers.First().HandleAsync(request, ct);
    }
}
