using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Messaging.Skeleton;

/// <summary>
///     Captures one slice of ambient state in the sending scope and rebuilds it in the new scope on
///     the far side of a boundary.
/// </summary>
/// <remarks>
///     Always resolved as a collection. An empty collection means there is nothing to rebuild, which
///     is how a boundary stays ignorant of whether any particular package is installed.
/// </remarks>
public interface IMessageContextPropagator
{
    /// <summary>Flattens this propagator's ambient state into <paramref name="items" />.</summary>
    /// <param name="items">
    ///     The dictionary being filled. Keys must be namespaced to this propagator so two
    ///     propagators cannot overwrite each other.
    /// </param>
    /// <param name="source">The provider of the scope the message is being sent from.</param>
    void Capture(IDictionary<string, string?> items, IServiceProvider source);

    /// <summary>Rebuilds this propagator's ambient state inside <paramref name="target" />.</summary>
    /// <param name="items">The flattened state produced by <see cref="Capture" />.</param>
    /// <param name="target">The provider of the scope the message is being handled in.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask RestoreAsync(
        IReadOnlyDictionary<string, string?> items,
        IServiceProvider                     target,
        CancellationToken                    ct = default);
}
