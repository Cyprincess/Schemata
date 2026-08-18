using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Transport.RabbitMq;
using Schemata.Transport.RabbitMq.Internal;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary><see cref="IServiceCollection" /> extensions that install the shared RabbitMQ transport.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the shared broker connection provider and the request/response correlation
    ///     tracker. Safe to call from every RabbitMQ-backed component: repeated calls resolve to the
    ///     same singletons, which is what keeps one connection per process.
    /// </summary>
    public static IServiceCollection AddRabbitMqTransport(
        this IServiceCollection            services,
        Action<RabbitMqConnectionOptions>? configure = null
    ) {
        services.TryAddSingleton<IRabbitMqConnectionProvider, RabbitMqConnectionProvider>();
        services.TryAddSingleton<CorrelationTracker>();

        if (configure is not null) {
            services.Configure(configure);
        }

        return services;
    }
}
