using System;
using System.Threading;

namespace Schemata.Scheduling.Skeleton.Services;

/// <summary>
///     Process-local index of the executions this host is currently running, used to deliver
///     cancellation to the job body or inline operation that owns an execution row.
/// </summary>
/// <remarks>
///     Registration transfers ownership of the <see cref="CancellationTokenSource" /> to the
///     registry. A second registration for a name that still has a local owner is rejected without
///     taking the supplied source, so ownership stays with that caller. Exactly one removal wins per
///     registration, and only the winner disposes the source: the registration handle when it
///     observes its own entry, or a direct <see cref="TryRemove" /> caller. Cancellation does not
///     remove the entry, so the running owner keeps its source until it releases the registration.
/// </remarks>
public interface IActiveExecutionRegistry
{
    /// <summary>
    ///     Registers <paramref name="source" /> under <paramref name="executionName" /> and returns
    ///     the handle that releases it. Disposing the handle removes the entry and disposes the
    ///     source when that registration is still present.
    /// </summary>
    /// <param name="executionName">Registry key of the execution.</param>
    /// <param name="source">Cancellation source owned by the execution body.</param>
    /// <returns>The ownership handle for <paramref name="source" />.</returns>
    /// <exception cref="InvalidOperationException">
    ///     <paramref name="executionName" /> already has a local owner. The rejected call leaves
    ///     <paramref name="source" /> with the caller, which must dispose it.
    /// </exception>
    IDisposable Register(string executionName, CancellationTokenSource source);

    /// <summary>
    ///     Removes <paramref name="executionName" />. The caller that receives
    ///     <see langword="true" /> owns <paramref name="source" /> and must dispose it.
    /// </summary>
    /// <param name="executionName">Registry key of the execution.</param>
    /// <param name="source">The removed source, or <see langword="null" /> when nothing was removed.</param>
    /// <returns><see langword="true" /> when this call removed the entry.</returns>
    bool TryRemove(string executionName, out CancellationTokenSource? source);

    /// <summary>
    ///     Cancels the registered source without removing it. Callbacks run synchronously on the
    ///     calling thread; <see cref="CancellationTokenSource" /> aggregates their failures into an
    ///     <see cref="AggregateException" /> that propagates to the caller.
    /// </summary>
    /// <param name="executionName">Registry key of the execution.</param>
    /// <returns>
    ///     <see langword="true" /> when a live source received cancellation;
    ///     <see langword="false" /> when no source is registered or the owner already disposed it.
    /// </returns>
    bool Cancel(string executionName);
}
