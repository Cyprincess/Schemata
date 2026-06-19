using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Event.Foundation.Builders;
using Schemata.Event.RabbitMq;
using Schemata.Event.RabbitMq.Internal;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="EventConsumerBuilder" /> extensions that enable RabbitMQ consumers.</summary>
public static class EventConsumerBuilderRabbitMqExtensions
{
    /// <summary>Registers the RabbitMQ consumer host and request/response correlation tracker.</summary>
    public static EventConsumerBuilder UseRabbitMq(
        this EventConsumerBuilder     builder,
        Action<RabbitMqEventOptions>? configure = null
    ) {
        builder.Services.TryAddSingleton<CorrelationTracker>();
        builder.Services.AddHostedService<RabbitMqConsumerHost>();

        if (configure != null) {
            builder.Services.Configure(configure);
        }

        return builder;
    }
}
