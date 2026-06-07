using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Sole audit/persistence hook for <see cref="IProcessRuntime" />.
///     Observers see the runtime's post-mutation view of the process for each
///     event.
/// </summary>
public interface IProcessLifecycleObserver
{
    /// <summary>Fires after <see cref="IProcessRuntime.StartProcessInstanceAsync" /> caches the instance.</summary>
    Task OnStartedAsync(SchemataProcess process, CancellationToken ct = default);

    /// <summary>Fires after each runtime-driven transition (Complete / Correlate / Signal / Trigger / Terminate).</summary>
    Task OnTransitionedAsync(
        SchemataProcess           process,
        SchemataProcessTransition transition,
        CancellationToken         ct = default
    );

    /// <summary>Fires after the runtime evicts a terminal instance from its cache.</summary>
    Task OnTerminatedAsync(SchemataProcess process, CancellationToken ct = default);
}
