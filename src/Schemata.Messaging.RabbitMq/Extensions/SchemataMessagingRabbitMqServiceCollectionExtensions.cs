using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Messaging.RabbitMq;
using Schemata.Messaging.RabbitMq.Runtime;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the RabbitMQ request dispatcher.</summary>
public static class SchemataMessagingRabbitMqServiceCollectionExtensions
{
    /// <summary>
    ///     Registers <see cref="IRequestDispatcher" />, <see cref="ICommandDispatcher" /> and
    ///     <see cref="IQueryDispatcher" /> backed by RabbitMQ, plus the consumer host when a queue
    ///     name is configured. Also self-registers the concrete <see cref="InProcessRequestDispatcher" />
    ///     (never assigned to the three dispatcher interfaces above, which RabbitMQ owns here) so the
    ///     consumer host can run a consumed request's local pipeline even when no domain module
    ///     (<c>AddSchemataFlow</c>, <c>AddSchemataResources</c>, ...) already registered it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the only entry point. There is deliberately no
    ///         <c>SchemataMessagingBuilder.UseRabbitMq(...)</c>: that chaining sugar would save one
    ///         <c>schema.ConfigureServices(...)</c> call at the cost of coupling this transport to a
    ///         builder type, which would end this package's ability to be used without the Schemata
    ///         lifecycle at all.
    ///     </para>
    ///     <para>
    ///         Inside a Schemata application, call it through
    ///         <c>schema.ConfigureServices(services =&gt; services.AddRabbitMqRequestDispatcher(...))</c>.
    ///         Staged registrations flush before any feature runs, so this <c>TryAdd</c> lands first
    ///         and beats the in-process default without <c>Replace</c> and without probing.
    ///     </para>
    ///     <para>
    ///         The caller must also have called <c>AddRabbitMqTransport(...)</c>; the shared
    ///         connection and correlation tracker come from there, and this package opens channels
    ///         but never a connection.
    ///     </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Topology, timeout and the mandatory wire-name registrations.</param>
    public static IServiceCollection AddRabbitMqRequestDispatcher(
        this IServiceCollection          services,
        Action<RabbitMqRequestOptions>   configure
    ) {
        services.Configure(configure);

        services.TryAddScoped<RabbitMqRequestDispatcher>();
        services.TryAddScoped<IRequestDispatcher>(sp => sp.GetRequiredService<RabbitMqRequestDispatcher>());
        services.TryAddScoped<ICommandDispatcher>(sp => sp.GetRequiredService<RabbitMqRequestDispatcher>());
        services.TryAddScoped<IQueryDispatcher>(sp => sp.GetRequiredService<RabbitMqRequestDispatcher>());

        // The consumer host resolves this concrete type directly (never the interfaces above) to
        // run a consumed request's local pipeline; self-register it so a standalone configuration
        // (RabbitMQ transport with no domain module) still has it available.
        services.TryAddScoped<InProcessRequestDispatcher>();

        services.AddHostedService<RabbitMqRequestConsumerHost>();

        return services;
    }
}
