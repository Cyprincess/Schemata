using System;
using System.Collections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Exceptions;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton.Services;

/// <summary>
///     Per-tenant <see cref="IServiceProvider" /> factory that caches one provider per tenant
///     holding tenant-specific singletons (tenant entity, accessor, registered overrides).
///     Non-overridden services resolve from the host root via
///     <see cref="TenantCompositeServiceProvider" />.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <remarks>
///     Tenant overrides must be Singleton; Scoped/Transient are rejected at build time.
///     Overrides win at top-level resolution only — services injected into host-scoped
///     constructors still see the host's view of their dependencies. Tenant-aware services
///     that must participate in injection chains should consult
///     <see cref="ITenantContextAccessor{TTenant}" /> at call time.
/// </remarks>
public class SchemataTenantServiceProviderFactory<TTenant> : ITenantServiceProviderFactory<TTenant>
    where TTenant : SchemataTenant
{
    private readonly ITenantProviderCache   _cache;
    private readonly SchemataTenancyOptions _options;
    private readonly IServiceProvider       _root;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataTenantServiceProviderFactory{TTenant}" /> class.
    /// </summary>
    public SchemataTenantServiceProviderFactory(
        IServiceProvider                 root,
        ITenantProviderCache             cache,
        IOptions<SchemataTenancyOptions> options
    ) {
        _root    = root;
        _cache   = cache;
        _options = options.Value;
    }

    #region ITenantServiceProviderFactory<TTenant> Members

    public ITenantProviderLease CreateServiceProvider(ITenantContextAccessor<TTenant> accessor) {
        if (accessor.Tenant is not { } tenant) {
            throw new TenantResolveException();
        }

        var id = tenant.Uid.ToString();

        return _cache.Lease(id, () => Build(id, tenant));
    }

    #endregion

    private IServiceProvider Build(string id, TTenant tenant) {
        IServiceCollection overrides = new ServiceCollection();

        overrides.AddSingleton(tenant);
        overrides.AddSingleton<ITenantContextAccessor<TTenant>>(_ => new TenantBoundContextAccessor<TTenant>(_root, tenant));

        if (_options.TenantOverrides.TryGetValue(id, out var tenantOverrides)) {
            foreach (var apply in tenantOverrides) {
                var snapshot = overrides.Count;
                apply(overrides);
                EnforceSingletonOverride(id, overrides, snapshot);
            }
        }

        foreach (var apply in _options.DynamicOverrides) {
            var snapshot = overrides.Count;
            apply(id, overrides, _root);
            EnforceSingletonOverride(id, overrides, snapshot);
        }

        var container = overrides.BuildServiceProvider();
        return new TenantCompositeServiceProvider(container, _root);
    }

    private static void EnforceSingletonOverride(string id, IServiceCollection container, int snapshot) {
        for (var i = snapshot; i < container.Count; i++) {
            var descriptor = container[i];
            if (descriptor.Lifetime == ServiceLifetime.Singleton) {
                continue;
            }

            throw new InvalidOperationException(
                $"Tenant override for '{id}' registered '{descriptor.ServiceType}' as {descriptor.Lifetime}; "
              + "tenant-specific registrations must be Singleton. Resolve Scoped/Transient services "
              + "from the host scope instead.");
        }
    }
}