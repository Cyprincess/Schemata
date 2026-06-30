using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Side-effect hook for token-level lifecycle events emitted by Flow runtimes.
/// </summary>
/// <remarks>
///     All methods provide a no-op default so implementations override only the hooks they care about.
    ///     Observer exceptions are isolated by the runtime; they never abort a transition.
/// </remarks>
public interface ITokenLifecycleObserver
{
    /// <summary>Fires when a parent token spawns a child at a fork, sub-process invocation, or boundary catch.</summary>
    Task OnTokenForkedAsync(
        SchemataProcess   process,
        TokenSnapshot     token,
        TokenSnapshot?    spawner,
        CancellationToken ct = default) {
        return Task.CompletedTask;
    }

    /// <summary>Fires when sibling tokens merge into a single output token at a join or merge gateway.</summary>
    Task OnTokenJoinedAsync(
        SchemataProcess              process,
        TokenSnapshot                output,
        IReadOnlyList<TokenSnapshot> inputs,
        CancellationToken            ct = default) {
        return Task.CompletedTask;
    }

    /// <summary>Fires when an advisor or activity throws and the token transitions to Failed.</summary>
    Task OnTokenFailedAsync(
        SchemataProcess   process,
        TokenSnapshot     token,
        Exception         exception,
        CancellationToken ct = default) {
        return Task.CompletedTask;
    }

    /// <summary>Fires when a token is explicitly cancelled (boundary interrupt, process termination, scope cancel).</summary>
    Task OnTokenCancelledAsync(
        SchemataProcess   process,
        TokenSnapshot     token,
        CancellationToken ct = default) {
        return Task.CompletedTask;
    }
}
