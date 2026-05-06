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
/// <remarks>
///     The root <see cref="IServiceCollection" /> is copied once per tenant (with the
///     <see cref="ITenantContextAccessor{TTenant}" /> bound to the tenant-scoped
///     accessor instance). Tenant-specific and dynamic overrides from
///     <see cref="SchemataTenancyOptions" /> are then applied before the provider is built.
///     AllOverrides are applied at registration time by the builder, so they are already
///     present in the copied root services.
/// </remarks>
public class SchemataTenantServiceProviderFactory<TTenant> : ITenantServiceProviderFactory<TTenant>
    where TTenant : SchemataTenant
{
    private readonly ITenantProviderCache   _cache;
    private readonly SchemataTenancyOptions _options;
    private readonly IServiceProvider       _root;
    private readonly IServiceCollection     _services;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataTenantServiceProviderFactory{TTenant}" /> class.
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

    #region ITenantServiceProviderFactory<TTenant> Members

    public IServiceProvider CreateServiceProvider(ITenantContextAccessor<TTenant> accessor) {
        if (accessor.Tenant?.Uid is not { } tenantKey) {
            throw new TenantResolveException();
        }

        var id = tenantKey.ToString();

        return _cache.GetOrAdd(id, () => Build(id, accessor));
    }

    #endregion

    private IServiceProvider Build(string id, ITenantContextAccessor<TTenant> accessor) {
        IServiceCollection container = new ServiceCollection();

        foreach (var service in _services) {
            if (service.ServiceType == typeof(ITenantContextAccessor<TTenant>)) {
                container.Add(ServiceDescriptor.Singleton(typeof(ITenantContextAccessor<TTenant>), accessor));

                continue;
            }

            if (typeof(ITenantContextAccessor<TTenant>).IsAssignableFrom(service.ServiceType)) {
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
