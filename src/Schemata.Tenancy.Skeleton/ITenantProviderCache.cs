using System;

namespace Schemata.Tenancy.Skeleton;

/// <summary>
///     Caches per-tenant <see cref="IServiceProvider" /> instances keyed by tenant identifier string.
/// </summary>
/// <remarks>
///     Implementations must dispose each evicted provider that implements <see cref="IDisposable" />
///     and enforce both a sliding expiration and a maximum capacity as configured via
///     <see cref="SchemataTenancyOptions" />.
/// </remarks>
public interface ITenantProviderCache
{
    /// <summary>Gets the cached provider for the tenant id or builds and stores one via <paramref name="factory" />.</summary>
    IServiceProvider GetOrAdd(string id, Func<IServiceProvider> factory);

    /// <summary>Removes the cached provider for the tenant id, disposing it if present.</summary>
    void Remove(string id);
}
