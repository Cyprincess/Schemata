using System;

namespace Schemata.Tenancy.Skeleton;

/// <summary>
///     A lease over a cached per-tenant <see cref="IServiceProvider" />.
/// </summary>
/// <remarks>
///     <para>
    ///         While at least one lease is held, the underlying provider remains pinned
    ///         after cache eviction or removal. The provider is disposed deterministically
    ///         once the entry is retired and the active lease count reaches zero.
///     </para>
///     <para>
    ///         <see cref="IDisposable.Dispose" /> is idempotent, so repeated disposal leaves
    ///         the active lease tally consistent.
///     </para>
/// </remarks>
public interface ITenantProviderLease : IDisposable
{
    /// <summary>Gets the cached per-tenant service provider held by this lease.</summary>
    IServiceProvider Provider { get; }
}
