using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Event.Foundation.Internal;
using Schemata.Event.Skeleton;

namespace Schemata.Event.Foundation.Builders;

/// <summary>Fluent builder for the consumer-side wiring of the event bus.</summary>
public sealed class EventConsumerBuilder
{
    /// <summary>Initializes a new <see cref="EventConsumerBuilder"/> over the supplied service collection.</summary>
    public EventConsumerBuilder(IServiceCollection services) { Services = services; }

    /// <summary>The underlying service collection the builder writes to.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Registers the in-process subscription store, handler resolver, and dispatch context.</summary>
    public EventConsumerBuilder UseInProcess() {
        Services.TryAddSingleton<IEventSubscriptionStore, InMemoryEventSubscriptionStore>();
        Services.TryAddScoped<HandlerResolver>();
        Services.TryAddScoped<IEventDispatchContext, EventDispatchContext>();
        return this;
    }
}
