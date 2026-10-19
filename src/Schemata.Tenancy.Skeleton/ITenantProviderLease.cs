using System;
using System.Threading.Tasks;

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
///         Disposal is idempotent in both forms, so repeated disposal leaves the active
///         lease tally consistent. <see cref="IAsyncDisposable.DisposeAsync" /> awaits
///         asynchronous provider disposal. <see cref="IDisposable.Dispose" /> backs shutdown
///         and scope teardown; it blocks on asynchronous disposal when the provider only
///         implements <see cref="IAsyncDisposable" />.
///     </para>
/// </remarks>
public interface ITenantProviderLease : IDisposable, IAsyncDisposable
{
    /// <summary>Gets the cached per-tenant service provider held by this lease.</summary>
    IServiceProvider Provider { get; }
}
