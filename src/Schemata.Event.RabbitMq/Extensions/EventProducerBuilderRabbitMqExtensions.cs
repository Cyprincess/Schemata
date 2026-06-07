using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Event.Foundation.Builders;
using Schemata.Event.RabbitMq;
using Schemata.Event.RabbitMq.Internal;
using Schemata.Event.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class EventProducerBuilderRabbitMqExtensions
{
    public static EventProducerBuilder UseRabbitMq(
        this EventProducerBuilder     builder,
        Action<RabbitMqEventOptions>? configure = null
    ) {
        builder.Services.TryAddSingleton<CorrelationTracker>();
        builder.Services.TryAddScoped<IEventBus, RabbitMqEventBus>();

        if (configure != null) {
            builder.Services.Configure(configure);
        }

        return builder;
    }
}
