using System;

namespace Schemata.Tenancy.Skeleton;

/// <summary>
///     Caches per-tenant <see cref="IServiceProvider" /> instances keyed by tenant identifier string.
/// </summary>
/// <remarks>
///     <para>
///         Returned <see cref="ITenantProviderLease" /> handles maintain a refcount on the
///         underlying provider. Eviction (capacity overflow, sliding expiration) and
///         <see cref="Remove" /> retire an entry but defer disposal until every outstanding
///         lease for that entry has been released.
///     </para>
///     <para>
///         Implementations must dispose retired providers that implement <see cref="IDisposable" />
///         exactly once, the moment the active lease count reaches zero.
///     </para>
/// </remarks>
public interface ITenantProviderCache
{
    /// <summary>
    ///     Returns a lease over the cached provider for <paramref name="id" />, building one via
    ///     <paramref name="factory" /> if no entry exists. The caller must dispose the returned
    ///     lease when the tenant scope it backs is disposed.
    /// </summary>
    ITenantProviderLease Lease(string id, Func<IServiceProvider> factory);

    /// <summary>
    ///     Retires the entry for <paramref name="id" /> from the cache. The provider is disposed
    ///     once the last outstanding lease is released.
    /// </summary>
    void Remove(string id);
}
