using System;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Tenancy.Skeleton;

/// <summary>
///     Caches tenant providers by identity and authoritative version.
/// </summary>
/// <remarks>
///     <para>
///         Returned <see cref="ITenantProviderLease" /> handles maintain a refcount on the
///         underlying provider. Eviction (capacity overflow, sliding expiration) and
///         <see cref="RemoveAsync" /> retire an entry but defer disposal until every outstanding
///         lease for that entry has been released.
///     </para>
///     <para>
///         Implementations must attempt disposal of retired providers that implement
///         <see cref="IDisposable" /> or <see cref="IAsyncDisposable" /> exactly once,
///         the moment the active lease count reaches zero.
///     </para>
///     <para>
///         Cleanup attempts every eligible retired provider before propagating failures.
///         A failed acquisition releases only its undelivered lease; outstanding handles
///         retain their provider until their own release.
///     </para>
/// </remarks>
public interface ITenantProviderCache
{
    /// <summary>
    ///     Returns a lease over the cached provider for <paramref name="id" /> at
    ///     <paramref name="version" />, building one via <paramref name="create" /> for a cache miss.
    ///     Concurrent misses for the same key share one build; waiters complete asynchronously and
    ///     observe <paramref name="ct" />. The caller must dispose the returned lease when the
    ///     tenant scope it backs is disposed.
    /// </summary>
    ValueTask<ITenantProviderLease> LeaseVersionAsync(
        string                 id,
        Guid                   version,
        Func<IServiceProvider> create,
        CancellationToken      ct = default
    );

    /// <summary>
    ///     Retires the entry for <paramref name="id" /> from the cache. The provider is disposed
    ///     once the last outstanding lease is released.
    /// </summary>
    ValueTask RemoveAsync(string id);
}
