using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Event.Skeleton;

namespace Schemata.Event.Foundation.Internal;

public sealed class HandlerResolver
{
    private readonly IServiceProvider _services;

    public HandlerResolver(IServiceProvider services) { _services = services; }

    public Task InvokeEventHandlersAsync<TEvent>(TEvent @event, CancellationToken ct)
        where TEvent : IEvent {
        var handlers = _services.GetServices<IEventHandler<TEvent>>().ToList();

        if (handlers.Count == 0) {
            throw new InvalidOperationException(
                $"No event handler registered for event type '{typeof(TEvent).FullName}'."
            );
        }

        var tasks = handlers.Select(h => h.HandleAsync(@event, ct));
        return Task.WhenAll(tasks);
    }

    public Task<TResponse> InvokeRequestHandlerAsync<TRequest, TResponse>(TRequest request, CancellationToken ct)
        where TRequest : IRequest<TResponse> {
        var handlers = _services.GetServices<IRequestHandler<TRequest, TResponse>>().ToList();

        if (handlers.Count == 0) {
            throw new InvalidOperationException(
                $"No request handler registered for request type '{typeof(TRequest).FullName}'."
            );
        }

        if (handlers.Count > 1) {
            throw new InvalidOperationException(
                $"Multiple request handlers registered for request type '{
                    typeof(TRequest).FullName
                }'. Expected exactly one."
            );
        }

        return handlers.First().HandleAsync(request, ct);
    }
}
