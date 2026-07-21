using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Side-effect hook for process-level lifecycle events fired by the runtime after persistence succeeds.
/// </summary>
/// <remarks>
///     All methods provide a no-op default so implementations only override the hooks they care about.
///     Observer exceptions are caught and logged by the runtime; they never abort a transition.
/// </remarks>
public interface IProcessLifecycleObserver
{
    /// <summary>Fires after a process instance is started and persisted.</summary>
    Task OnStartedAsync(SchemataProcess process, CancellationToken ct = default) {
        return Task.CompletedTask;
    }

    /// <summary>Fires after each runtime-driven transition (Complete / Correlate / Signal / Trigger / Terminate).</summary>
    Task OnTransitionedAsync(
        SchemataProcess           process,
        SchemataProcessTransition transition,
        CancellationToken         ct = default) {
        return Task.CompletedTask;
    }

    /// <summary>Fires after a process instance reaches a terminal state (Completed / Terminated / Cancelled).</summary>
    Task OnTerminatedAsync(SchemataProcess process, CancellationToken ct = default) {
        return Task.CompletedTask;
    }

    /// <summary>Fires when a runtime invocation throws and the process transitions to Failed.</summary>
    Task OnFailedAsync(SchemataProcess process, Exception exception, CancellationToken ct = default) {
        return Task.CompletedTask;
    }
}
