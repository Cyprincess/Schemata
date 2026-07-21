using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Exceptions;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Services;

/// <summary>
///     Per-tenant <see cref="IServiceProvider" /> factory that caches one provider per tenant
///     holding tenant-specific singletons (tenant entity, accessor, registered overrides).
///     Host services resolve through <see cref="TenantCompositeServiceProvider" />.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <remarks>
///     <para>
///         Tenant overrides must be Singleton; Scoped/Transient and open-generic descriptors are
///         rejected while building the tenant container. Tenant singleton constructors may safely
///         capture host singletons. A host Scoped or Transient service resolved through fallback is
///         retained by the tenant singleton for the tenant provider's lifetime.
///     </para>
///     <para>
///         Overrides win for tenant-side top-level resolution. Host service constructors resolve
///         from the host container, so their dependencies are not tenant-overridable. Tenant-aware
///         host services should consult <see cref="ITenantContextAccessor{TTenant}" /> at call time.
///         <see cref="ITenantContextAccessor{TTenant}.GetBaseServiceProviderAsync" /> returns the
///         host root provider.
///     </para>
///     <para>
///         Keyed services resolve from tenant overrides before the host root. A keyed
///         <see cref="System.Collections.Generic.IEnumerable{T}" /> resolves the tenant collection
///         when it contains a matching key and otherwise resolves the host collection. Non-keyed
///         <see cref="System.Collections.Generic.IEnumerable{T}" /> resolution keeps host-first
///         concatenation with tenant additions after it.
///     </para>
/// </remarks>
public class SchemataTenantServiceProviderFactory<TTenant> : ITenantServiceProviderFactory<TTenant>
    where TTenant : SchemataTenant
{
    private readonly ITenantProviderCache   _cache;
    private readonly SchemataTenancyOptions _options;
    private readonly IServiceProvider       _root;

    /// <summary>Creates a factory that builds and caches tenant-specific service providers.</summary>
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
                apply(overrides);
            }
        }

        foreach (var apply in _options.DynamicOverrides) {
            apply(id, overrides, _root);
        }

        TenantCompositeServiceProvider composite = null!;
        ValidateAndWrapOverrides(id, overrides, () => composite);

        var container = overrides.BuildServiceProvider();
        composite = new(container, _root);
        return composite;
    }

    private static void ValidateAndWrapOverrides(
        string                 id,
        IServiceCollection     overrides,
        Func<IServiceProvider> composite
    ) {
        for (var i = 0; i < overrides.Count; i++) {
            var descriptor = overrides[i];
            ValidateDescriptor(id, descriptor);
            overrides[i] = WrapDescriptor(descriptor, composite);
        }
    }

    private static void ValidateDescriptor(string id, ServiceDescriptor descriptor) {
        if (descriptor.Lifetime != ServiceLifetime.Singleton) {
            throw new InvalidOperationException(
                $"Tenant override for '{id}' registered '{descriptor.ServiceType}' as {descriptor.Lifetime}; "
              + "tenant-specific registrations must be Singleton. Resolve Scoped/Transient services "
              + "from the host scope instead.");
        }

        var implementationType = descriptor.IsKeyedService
            ? descriptor.KeyedImplementationType
            : descriptor.ImplementationType;
        if (descriptor.ServiceType.ContainsGenericParameters || implementationType?.ContainsGenericParameters == true) {
            throw new InvalidOperationException(
                $"Tenant override for '{id}' registered open-generic service '{descriptor.ServiceType}'; "
              + "tenant-specific registrations must be closed types.");
        }
    }

    private static ServiceDescriptor WrapDescriptor(
        ServiceDescriptor      descriptor,
        Func<IServiceProvider> composite
    ) {
        if (descriptor.IsKeyedService) {
            if (descriptor.KeyedImplementationType is { } implementationType) {
                return ServiceDescriptor.KeyedSingleton(
                    descriptor.ServiceType,
                    descriptor.ServiceKey,
                    (_, _) => ActivatorUtilities.CreateInstance(composite(), implementationType)
                );
            }

            if (descriptor.KeyedImplementationFactory is { } factory) {
                return ServiceDescriptor.KeyedSingleton(
                    descriptor.ServiceType,
                    descriptor.ServiceKey,
                    (_, key) => factory(composite(), key)
                );
            }

            return descriptor;
        }

        if (descriptor.ImplementationType is { } type) {
            return ServiceDescriptor.Singleton(
                descriptor.ServiceType,
                _ => ActivatorUtilities.CreateInstance(composite(), type)
            );
        }

        if (descriptor.ImplementationFactory is { } implementationFactory) {
            return ServiceDescriptor.Singleton(descriptor.ServiceType, _ => implementationFactory(composite()));
        }

        return descriptor;
    }
}
