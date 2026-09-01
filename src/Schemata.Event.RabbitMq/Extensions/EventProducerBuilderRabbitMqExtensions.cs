using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Event.Foundation.Builders;
using Schemata.Event.RabbitMq;
using Schemata.Event.RabbitMq.Runtime;
using Schemata.Event.Skeleton;
using Schemata.Transport.RabbitMq;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="EventProducerBuilder" /> extensions that enable RabbitMQ publishers.</summary>
public static class EventProducerBuilderRabbitMqExtensions
{
    /// <summary>Registers the RabbitMQ event bus and outbox publisher over the shared transport.</summary>
    /// <param name="builder">The producer builder being configured.</param>
    /// <param name="configure">Topology settings for the exchange, queue and dead-letter routing.</param>
    /// <param name="connection">Broker connection settings shared with every other RabbitMQ client.</param>
    public static EventProducerBuilder UseRabbitMq(
        this EventProducerBuilder          builder,
        Action<RabbitMqEventOptions>?      configure  = null,
        Action<RabbitMqConnectionOptions>? connection = null
    ) {
        builder.Services.AddRabbitMqTransport(connection);

        builder.Services.TryAddScoped<IEventBus, RabbitMqEventBus>();

        // The Event feature's outbox dispatcher replays Pending rows through this publisher after
        // broker publish failures.
        builder.Services.TryAddSingleton<IEventOutboxPublisher, RabbitMqEventOutboxPublisher>();

        if (configure != null) {
            builder.Services.Configure(configure);
        }

        return builder;
    }
}
