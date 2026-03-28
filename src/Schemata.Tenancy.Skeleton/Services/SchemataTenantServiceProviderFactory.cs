using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton.Services;

/// <summary>
///     Base class holding the shared cache of per-tenant service providers.
/// </summary>
public class SchemataTenantServiceProviderFactory
{
    /// <summary>Thread-safe cache of lazily-initialized per-tenant service providers, keyed by tenant ID.</summary>
    protected static readonly ConcurrentDictionary<string, Lazy<IServiceProvider>> Providers = [];
}

/// <summary>
///     Creates and caches isolated <see cref="IServiceProvider" /> instances for each tenant.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
/// <remarks>
///     Each tenant's container is a copy of the root service collection with tenant-specific
///     overrides applied. Containers are lazily built and cached for the lifetime of the application.
/// </remarks>
public class SchemataTenantServiceProviderFactory<TTenant, TKey> : SchemataTenantServiceProviderFactory,
                                                                   ITenantServiceProviderFactory<TTenant, TKey>
    where TTenant : SchemataTenant<TKey>
    where TKey : struct, IEquatable<TKey>
{
    private readonly Action<IServiceCollection, TTenant?> _configure;
    private readonly IServiceCollection                   _services;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataTenantServiceProviderFactory{TTenant, TKey}" /> class.
    /// </summary>
    public SchemataTenantServiceProviderFactory(
        IServiceCollection                   services,
        Action<IServiceCollection, TTenant?> configure
    ) {
        _services  = services;
        _configure = configure;
    }

    #region ITenantServiceProviderFactory<TTenant,TKey> Members

    /// <inheritdoc />
    public IServiceProvider CreateServiceProvider(ITenantContextAccessor<TTenant, TKey> accessor) {
        var id = accessor.Tenant?.TenantId?.ToString();

        if (string.IsNullOrWhiteSpace(id)) {
            throw new InvalidOperationException(SchemataResources.GetResourceString(SchemataResources.ST1027));
        }

        // TODO: avoid resolving IServiceProvider for non-existing tenant, it may cause memory leak or DoS attack.

        return Providers.GetOrAdd(id!, _ => new(() => {
                             var container = new ServiceCollection();

                             foreach (var service in _services) {
                                 if (service.ServiceType == typeof(ITenantContextAccessor<TTenant, TKey>)) {
                                     container.TryAddSingleton(accessor);

                                     continue;
                                 }

                                 if (typeof(ITenantContextAccessor<TTenant, TKey>).IsAssignableFrom(service.ServiceType)) {
                                     continue;
                                 }

                                 container.Add(service);
                             }

                             _configure(container, accessor.Tenant);

                             return container.BuildServiceProvider();
                         }))
                        .Value;
    }

    #endregion
}
