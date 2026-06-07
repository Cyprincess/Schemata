using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Event.Foundation.Internal;
using Schemata.Event.Skeleton;

namespace Schemata.Event.Foundation.Builders;

/// <summary>Fluent builder for the producer-side wiring of the event bus.</summary>
public sealed class EventProducerBuilder
{
    /// <summary>Initializes a new <see cref="EventProducerBuilder"/> over the supplied service collection.</summary>
    public EventProducerBuilder(IServiceCollection services) { Services = services; }

    /// <summary>The underlying service collection the builder writes to.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Registers the in-process <see cref="IEventBus"/> implementation.</summary>
    public EventProducerBuilder UseInProcess() {
        Services.TryAddScoped<IEventBus, InProcessEventBus>();
        return this;
    }
}
