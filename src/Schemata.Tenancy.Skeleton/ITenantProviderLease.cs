using System;

namespace Schemata.Tenancy.Skeleton;

/// <summary>
///     A lease over a cached per-tenant <see cref="IServiceProvider" />.
/// </summary>
/// <remarks>
///     <para>
///         While at least one lease is held, the underlying provider is pinned and will
///         not be disposed even if the cache retires the entry (eviction or removal).
///         The provider is disposed deterministically once the entry is retired and
///         the active lease count reaches zero.
///     </para>
///     <para>
///         <see cref="IDisposable.Dispose" /> is idempotent — disposing the same lease
///         twice does not under-count the active lease tally.
///     </para>
/// </remarks>
public interface ITenantProviderLease : IDisposable
{
    /// <summary>Gets the cached per-tenant service provider held by this lease.</summary>
    IServiceProvider Provider { get; }
}
