using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Foundation.Builders;

/// <summary>
///     Fluent builder for the Push feature, closed on the host-selected subscription entity.
///     Transports are registered into the <see cref="IPushTransport" /> collection so
///     <see cref="DefaultPushService" /> can resolve and fan out to all of them; the transport's own
///     <see cref="IPushTransport.Name" /> identifies it.
/// </summary>
public sealed class SchemataPushBuilder
{
    /// <summary>Initializes the builder bound to the given options and service collection.</summary>
    /// <param name="schemata">The <see cref="SchemataOptions" />.</param>
    /// <param name="services">The service collection that receives transports.</param>
    public SchemataPushBuilder(SchemataOptions schemata, IServiceCollection services)
        : this(schemata, services, typeof(SchemataPushSubscription)) { }

    internal SchemataPushBuilder(SchemataOptions schemata, IServiceCollection services, Type subscriptionType) {
        Schemata         = schemata;
        Services         = services;
        SubscriptionType = subscriptionType;
    }

    private SchemataOptions Schemata { get; }

    /// <summary>Service collection that receives transport registrations.</summary>
    public IServiceCollection Services { get; }

    /// <summary>The subscription entity type the Push feature is closed on.</summary>
    public Type SubscriptionType { get; }

    /// <summary>
    ///     Adds a feature to the Schemata configuration.
    /// </summary>
    /// <typeparam name="T">The <see cref="ISimpleFeature" /> type.</typeparam>
    public void AddFeature<T>()
        where T : ISimpleFeature {
        AddFeature(typeof(T));
    }

    /// <summary>
    ///     Adds a feature by its runtime type to the Schemata configuration.
    /// </summary>
    /// <param name="type">The concrete <see cref="ISimpleFeature" /> type.</param>
    public void AddFeature(Type type) {
        Schemata.AddFeature(type);
    }

    /// <summary>
    ///     Registers <typeparamref name="TTransport" /> in the fan-out transport collection. The
    ///     transport reports its own <see cref="IPushTransport.Name" />; no separate key is stored.
    /// </summary>
    /// <typeparam name="TTransport">The transport implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public SchemataPushBuilder AddTransport<TTransport>()
        where TTransport : class, IPushTransport {
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IPushTransport, TTransport>());
        return this;
    }
}
