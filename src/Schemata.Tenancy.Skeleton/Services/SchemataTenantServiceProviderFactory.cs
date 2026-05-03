using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Exceptions;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton.Services;

/// <summary>
///     Creates and caches isolated <see cref="IServiceProvider" /> instances for each tenant.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
/// <remarks>
///     The root <see cref="IServiceCollection" /> is copied once per tenant (with the
///     <see cref="ITenantContextAccessor{TTenant,TKey}" /> bound to the tenant-scoped
///     accessor instance). Tenant-specific and dynamic overrides from
///     <see cref="SchemataTenancyOptions" /> are then applied before the provider is built.
///     AllOverrides are applied at registration time by the builder, so they are already
///     present in the copied root services.
/// </remarks>
public class SchemataTenantServiceProviderFactory<TTenant, TKey> : ITenantServiceProviderFactory<TTenant, TKey>
    where TTenant : SchemataTenant<TKey>
    where TKey : struct, IEquatable<TKey>
{
    private readonly ITenantProviderCache   _cache;
    private readonly SchemataTenancyOptions _options;
    private readonly IServiceProvider       _root;
    private readonly IServiceCollection     _services;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataTenantServiceProviderFactory{TTenant, TKey}" /> class.
    /// </summary>
    public SchemataTenantServiceProviderFactory(
        IServiceCollection               services,
        IServiceProvider                 root,
        ITenantProviderCache             cache,
        IOptions<SchemataTenancyOptions> options
    ) {
        _services = services;
        _root     = root;
        _cache    = cache;
        _options  = options.Value;
    }

    #region ITenantServiceProviderFactory<TTenant,TKey> Members

    /// <inheritdoc />
    public IServiceProvider CreateServiceProvider(ITenantContextAccessor<TTenant, TKey> accessor) {
        if (accessor.Tenant?.TenantId is not { } tenantKey) {
            throw new TenantResolveException();
        }

        var id = tenantKey.ToString()!;

        return _cache.GetOrAdd(id, () => Build(id, accessor));
    }

    #endregion

    private IServiceProvider Build(string id, ITenantContextAccessor<TTenant, TKey> accessor) {
        IServiceCollection container = new ServiceCollection();

        foreach (var service in _services) {
            if (service.ServiceType == typeof(ITenantContextAccessor<TTenant, TKey>)) {
                container.Add(ServiceDescriptor.Singleton(typeof(ITenantContextAccessor<TTenant, TKey>), accessor));

                continue;
            }

            if (typeof(ITenantContextAccessor<TTenant, TKey>).IsAssignableFrom(service.ServiceType)) {
                continue;
            }

            container.Add(service);
        }

        if (_options.TenantOverrides.TryGetValue(id, out var tenantOverrides)) {
            foreach (var o in tenantOverrides) {
                o(container);
            }
        }

        foreach (var o in _options.DynamicOverrides) {
            o(id, container, _root);
        }

        return container.BuildServiceProvider();
    }
}
