using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>Side-effect hook for process lifecycle events after runtime persistence succeeds.</summary>
public interface IProcessLifecycleObserver
{
    /// <summary>Fires after <see cref="IProcessRuntime.StartProcessInstanceAsync" /> persists and caches the instance.</summary>
    Task OnStartedAsync(SchemataProcess process, CancellationToken ct = default);

    /// <summary>Fires after each runtime-driven transition (Complete / Correlate / Signal / Trigger / Terminate).</summary>
    Task OnTransitionedAsync(
        SchemataProcess           process,
        SchemataProcessTransition transition,
        CancellationToken         ct = default
    );

    /// <summary>Fires after the runtime evicts a terminal instance from its cache.</summary>
    Task OnTerminatedAsync(SchemataProcess process, CancellationToken ct = default);

    /// <summary>Fires after process runtime execution fails.</summary>
    Task OnFailedAsync(SchemataProcess process, Exception exception, CancellationToken ct = default);
}
