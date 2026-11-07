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
///         asynchronous provider disposal. <see cref="IDisposable.Dispose" /> preserves synchronous
///         disposal exceptions and throws <see cref="InvalidOperationException" /> for an async-only
///         provider, with an instruction to call <see cref="IAsyncDisposable.DisposeAsync" />.
///     </para>
/// </remarks>
public interface ITenantProviderLease : IDisposable, IAsyncDisposable
{
    /// <summary>Gets the cached per-tenant service provider held by this lease.</summary>
    IServiceProvider Provider { get; }
}
