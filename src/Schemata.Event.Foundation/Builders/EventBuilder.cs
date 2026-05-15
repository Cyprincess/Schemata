using Microsoft.Extensions.DependencyInjection;
using Schemata.Event.Skeleton;

namespace Schemata.Event.Foundation.Builders;

public sealed class EventBuilder
{
    internal readonly IServiceCollection Services;

    public EventBuilder(IServiceCollection services) { Services = services; }

    public EventBuilder UseHandler<TEvent, THandler>()
        where TEvent : IEvent
        where THandler : class, IEventHandler<TEvent> {
        Services.AddScoped(typeof(IEventHandler<>).MakeGenericType(typeof(TEvent)), typeof(THandler));
        return this;
    }

    public EventBuilder UseHandler<TRequest, TResponse, THandler>()
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse> {
        Services.AddScoped(
            typeof(IRequestHandler<,>).MakeGenericType(typeof(TRequest), typeof(TResponse)),
            typeof(THandler)
        );
        return this;
    }
}
