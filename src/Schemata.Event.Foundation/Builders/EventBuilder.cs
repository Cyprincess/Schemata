using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Event.Foundation.Internal;
using Schemata.Event.Skeleton;

namespace Schemata.Event.Foundation.Builders;

/// <summary>Fluent entry point for configuring event publish, consume, and routing.</summary>
public sealed class EventBuilder
{
    /// <summary>Initializes a new <see cref="EventBuilder"/> over the supplied service collection.</summary>
    public EventBuilder(IServiceCollection services) { Services = services; }

    /// <summary>The underlying service collection that downstream builders populate.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    ///     Registers an event/request type with the wire name that publishers and consumers
    ///     use to route the payload. Names form a distributed contract - see
    ///     <see cref="IEventTypeRegistry" /> for the design rationale. Unregistered types
    ///     throw on publish.
    /// </summary>
    public EventBuilder RegisterEvent<TEvent>(string name) {
        Services.AddSingleton<IPostConfigureOptions<EventTypeRegistryConfiguration>>(new RegisterEventConfiguration(typeof(TEvent), name));
        return this;
    }

    /// <summary>Configures the producer side of the event bus via an <see cref="EventProducerBuilder"/>.</summary>
    public EventBuilder UseProducer(Action<EventProducerBuilder>? configure = null) {
        var builder = new EventProducerBuilder(Services);
        configure?.Invoke(builder);
        return this;
    }

    /// <summary>Configures the consumer side of the event bus via an <see cref="EventConsumerBuilder"/>.</summary>
    public EventBuilder UseConsumer(Action<EventConsumerBuilder>? configure = null) {
        var builder = new EventConsumerBuilder(Services);
        configure?.Invoke(builder);
        return this;
    }

    /// <summary>Registers <typeparamref name="THandler"/> as a scoped <see cref="IEventHandler{TEvent}"/>.</summary>
    public EventBuilder UseHandler<TEvent, THandler>()
        where TEvent : IEvent
        where THandler : class, IEventHandler<TEvent> {
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IEventHandler<>).MakeGenericType(typeof(TEvent)), typeof(THandler)));
        return this;
    }

    /// <summary>Registers <typeparamref name="THandler"/> as a scoped <see cref="IRequestHandler{TRequest, TResponse}"/>.</summary>
    public EventBuilder UseHandler<TRequest, TResponse, THandler>()
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse> {
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestHandler<,>).MakeGenericType(typeof(TRequest), typeof(TResponse)), typeof(THandler)));
        return this;
    }

    /// <summary>Sets the <see cref="EventRouting"/> mode for <typeparamref name="TEvent"/>.</summary>
    public EventBuilder ConfigureRouting<TEvent>(EventRouting routing)
        where TEvent : IEvent {
        Services.Configure<SchemataEventOptions>(options => options.RoutingTable[typeof(TEvent)] = routing);
        return this;
    }
}
