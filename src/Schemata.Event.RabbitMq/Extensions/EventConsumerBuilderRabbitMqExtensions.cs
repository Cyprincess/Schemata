using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Event.Foundation.Builders;
using Schemata.Event.RabbitMq;
using Schemata.Event.RabbitMq.Internal;
using Schemata.Transport.RabbitMq;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="EventConsumerBuilder" /> extensions that enable RabbitMQ consumers.</summary>
public static class EventConsumerBuilderRabbitMqExtensions
{
    /// <summary>Registers the RabbitMQ consumer host over the shared transport.</summary>
    /// <param name="builder">The consumer builder being configured.</param>
    /// <param name="configure">Topology settings for the exchange, queue and dead-letter routing.</param>
    /// <param name="connection">Broker connection settings shared with every other RabbitMQ client.</param>
    public static EventConsumerBuilder UseRabbitMq(
        this EventConsumerBuilder          builder,
        Action<RabbitMqEventOptions>?      configure  = null,
        Action<RabbitMqConnectionOptions>? connection = null
    ) {
        builder.Services.AddRabbitMqTransport(connection);

        builder.Services.AddHostedService<RabbitMqConsumerHost>();

        if (configure != null) {
            builder.Services.Configure(configure);
        }

        return builder;
    }
}
