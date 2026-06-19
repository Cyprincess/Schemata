using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Event.Foundation.Builders;
using Schemata.Event.RabbitMq;
using Schemata.Event.RabbitMq.Internal;
using Schemata.Event.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="EventProducerBuilder" /> extensions that enable RabbitMQ publishers.</summary>
public static class EventProducerBuilderRabbitMqExtensions
{
    /// <summary>Registers the RabbitMQ event bus, outbox publisher, and correlation tracker.</summary>
    public static EventProducerBuilder UseRabbitMq(
        this EventProducerBuilder     builder,
        Action<RabbitMqEventOptions>? configure = null
    ) {
        builder.Services.TryAddSingleton<CorrelationTracker>();
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
