using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Bpmn.Runtime;

/// <summary>
///     Side-effect hook for BPMN compensation events. Compensation is a BPMN-only concept,
///     so the hook ships with the BPMN engine package.
/// </summary>
/// <remarks>
///     Default no-op implementations let consumers override only the hooks they need.
///     The engine swallows observer exceptions, so a failing observer leaves the transition
///     intact; observers own their error reporting.
/// </remarks>
public interface ICompensationLifecycleObserver
{
    /// <summary>Fires when a transaction sub-process or compensation event triggers a compensation handler.</summary>
    Task OnCompensationStartedAsync(
        SchemataProcess   process,
        TokenSnapshot     scope,
        CancellationToken ct = default) {
        return Task.CompletedTask;
    }

    /// <summary>Fires when the compensation handlers selected for the invocation run to completion.</summary>
    Task OnCompensationCompletedAsync(
        SchemataProcess   process,
        TokenSnapshot     scope,
        CancellationToken ct = default) {
        return Task.CompletedTask;
    }
}
