using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Caching.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Caching;

/// <summary>
///     Registration-only builder handed to <see cref="TenantCacheExtensions.UseCache{TTenant}" />
///     callbacks while a tenant service collection is composed. One cache mode per tenant outlet:
///     <see cref="UseShared" /> borrows an application-owned backend that no container disposes,
///     <see cref="UseDedicated" /> hands the created backend to the tenant provider, which disposes
///     it only after the retired provider's last lease is released. Repeating the identical
///     declaration keeps the single composed outlet; a different declaration for the same outlet
///     is rejected.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class TenantCacheBuilder<TTenant>
    where TTenant : SchemataTenant
{
    public TenantCacheBuilder(IServiceCollection services, Guid tenantUid) { Services = services; TenantUid = tenantUid; }

    /// <summary>Gets the tenant service collection receiving per-tenant registrations.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Gets the verified tenant identifier every registration is bound to.</summary>
    public Guid TenantUid { get; }

    /// <summary>
    ///     Borrows a fixed backend for this tenant's namespace. The application retains ownership:
    ///     instance registrations are never disposed by a container, so sharing the same backend
    ///     across outlets or retiring this tenant never releases it.
    /// </summary>
    public TenantCacheBuilder<TTenant> UseShared(ICacheBackend backend) {
        ArgumentNullException.ThrowIfNull(backend);

        return Declare(new Declaration("shared", backend), () => Services.AddSingleton(backend));
    }

    /// <summary>
    ///     Binds a backend created per tenant provider. The factory runs once when the tenant
    ///     container is composed; the container owns the instance and disposes it when the retired
    ///     provider's last lease is released. The same backend and prefix shared with another
    ///     outlet is a borrowed store, not a dedicated one.
    /// </summary>
    public TenantCacheBuilder<TTenant> UseDedicated(Func<IServiceProvider, ICacheBackend> create) {
        ArgumentNullException.ThrowIfNull(create);

        return Declare(new Declaration("dedicated", create), () => Services.AddSingleton<ICacheBackend>(create));
    }

    private void AddOutlet() {
        var normalizer = new TenantCacheKeyNormalizer(TenantUid);
        Services.TryAddSingleton<ICacheProvider>(sp => new CacheProvider(
            sp.GetRequiredService<ICacheBackend>(),
            normalizer));
        Services.TryAddSingleton<ISynchronousCacheBackend>(sp => new TenantSynchronousCacheBinding(
            sp.GetRequiredService<ICacheBackend>(),
            normalizer));
    }

    private TenantCacheBuilder<TTenant> Declare(Declaration declaration, Action register) {
        var existing = Services.Select(descriptor
            => (descriptor.IsKeyedService ? descriptor.KeyedImplementationInstance : descriptor.ImplementationInstance) as Declaration)
            .FirstOrDefault(item => item is not null);
        if (existing is not null) {
            if (existing != declaration) {
                throw new InvalidOperationException(
                    $"Tenant cache outlet for '{TenantUid}' is already declared as {existing}; refusing the conflicting {declaration}.");
            }

            return this;
        }

        Services.AddSingleton(declaration);
        register();
        AddOutlet();
        return this;
    }

    private sealed record Declaration(string Mode, object Target)
    {
        public override string ToString() { return Mode; }
    }

    /// <summary>
    ///     Tenant-namespaced binding of the optional synchronous capability. Every outlet binds it
    ///     so a host capability can never resolve into a tenant namespace. The binding takes
    ///     logical keys and applies the tenant frame; a backend without bounded synchronous access
    ///     keeps the binding and rejects each operation with <see cref="NotSupportedException" />,
    ///     so resolving the capability does not by itself prove that synchronous access works.
    /// </summary>
    private sealed class TenantSynchronousCacheBinding : ISynchronousCacheBackend
    {
        private readonly TenantCacheKeyNormalizer   _normalizer;
        private readonly ISynchronousCacheBackend? _backend;

        public TenantSynchronousCacheBinding(ICacheBackend backend, TenantCacheKeyNormalizer normalizer) {
            _backend    = backend as ISynchronousCacheBackend;
            _normalizer = normalizer;
        }

        public byte[]? Get(string key) { return Backend.Get(_normalizer.Normalize(key)); }

        public void Set(string key, byte[] value, CacheEntryOptions options) {
            Backend.Set(_normalizer.Normalize(key), value, options);
        }

        public void Refresh(string key) { Backend.Refresh(_normalizer.Normalize(key)); }

        public void Remove(string key) { Backend.Remove(_normalizer.Normalize(key)); }

        private ISynchronousCacheBackend Backend {
            get {
                return _backend ?? throw new NotSupportedException(
                    "The selected cache backend performs asynchronous I/O only; synchronous cache access requires a bounded synchronous backend.");
            }
        }
    }
}
