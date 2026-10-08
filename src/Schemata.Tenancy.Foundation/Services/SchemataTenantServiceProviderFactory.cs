using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Exceptions;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Services;

/// <summary>
///     Acquires version-keyed tenant providers after an authoritative lookup in a fresh host scope.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <remarks>
///     <para>
///         Closed tenant registrations retain Singleton, Scoped or Transient lifetime.
///         Tenant singleton dependencies resolve through the root composite; scoped and transient
///         dependencies resolve through their paired tenant/host scope.
///     </para>
///     <para>
///         Overrides win for tenant-side top-level resolution. Host service constructors resolve
///         from the host container, so their dependencies are not tenant-overridable.
///         Host singletons can read immutable TenantContext.Current at call time.
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

    public async ValueTask<ITenantProviderLease> CreateServiceProviderAsync(Guid identifier, CancellationToken ct = default) {
        TTenant? tenant;
        await using (var bootstrap = _root.CreateAsyncScope()) {
            var manager = bootstrap.ServiceProvider.GetRequiredService<ITenantManager<TTenant>>();
            tenant = await manager.FindByTenantId(identifier, ct);
        }
        if (tenant is null) throw new TenantResolveException();
        var id = tenant.Uid.ToString();
        return _cache.Lease(id, tenant.Timestamp, () => Build(id, tenant));
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
        overrides.AddScoped(_ => new TenantResolutionContext { Services = composite });

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
                return new ServiceDescriptor(
                    descriptor.ServiceType, descriptor.ServiceKey,
                    (provider, _) => ActivatorUtilities.CreateInstance(
                        descriptor.Lifetime == ServiceLifetime.Singleton ? composite() : provider.GetRequiredService<TenantResolutionContext>().Services,
                        implementationType), descriptor.Lifetime);
            }

            if (descriptor.KeyedImplementationFactory is { } factory) {
                return new ServiceDescriptor(
                    descriptor.ServiceType, descriptor.ServiceKey,
                    (provider, key) => factory(
                        descriptor.Lifetime == ServiceLifetime.Singleton ? composite() : provider.GetRequiredService<TenantResolutionContext>().Services,
                        key), descriptor.Lifetime);
            }

            return descriptor;
        }

        if (descriptor.ImplementationType is { } type) {
            return new ServiceDescriptor(descriptor.ServiceType,
                provider => ActivatorUtilities.CreateInstance(
                    descriptor.Lifetime == ServiceLifetime.Singleton ? composite() : provider.GetRequiredService<TenantResolutionContext>().Services,
                    type), descriptor.Lifetime);
        }

        if (descriptor.ImplementationFactory is { } implementationFactory) {
            return new ServiceDescriptor(descriptor.ServiceType,
                provider => implementationFactory(descriptor.Lifetime == ServiceLifetime.Singleton
                    ? composite() : provider.GetRequiredService<TenantResolutionContext>().Services), descriptor.Lifetime);
        }

        return descriptor;
    }
}
