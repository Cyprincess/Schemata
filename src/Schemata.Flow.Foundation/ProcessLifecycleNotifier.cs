using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Foundation;

/// <summary>
///     Concrete helper that fans process lifecycle events out to every registered
///     <see cref="IProcessLifecycleObserver" />. Observer exceptions are caught and logged at
///     <see cref="LogLevel.Error" />; they never abort a transition.
/// </summary>
public sealed class ProcessLifecycleNotifier
{
    private readonly ILogger<ProcessLifecycleNotifier> _logger;
    private readonly IList<IProcessLifecycleObserver> _processObservers;

    /// <summary>Creates a notifier bound to the resolved observer lists.</summary>
    public ProcessLifecycleNotifier(
        IEnumerable<IProcessLifecycleObserver> processObservers,
        ILogger<ProcessLifecycleNotifier>      logger
    ) {
        _processObservers = processObservers.ToList();
        _logger           = logger;
    }

    /// <summary>Fires <see cref="IProcessLifecycleObserver.OnStartedAsync" /> for every registered observer.</summary>
    public ValueTask NotifyStartedAsync(ProcessSnapshot snapshot, CancellationToken ct) {
        return NotifyAsync(nameof(IProcessLifecycleObserver.OnStartedAsync),
                           snapshot.Process,
                           observer => observer.OnStartedAsync(snapshot.Process, ct));
    }

    /// <summary>Fires <see cref="IProcessLifecycleObserver.OnTransitionedAsync" /> for every transition in <paramref name="snapshot" />.</summary>
    public async ValueTask NotifyTransitionedAsync(ProcessSnapshot snapshot, CancellationToken ct) {
        foreach (var transition in snapshot.Transitions) {
            await NotifyAsync(nameof(IProcessLifecycleObserver.OnTransitionedAsync),
                              snapshot.Process,
                              observer => observer.OnTransitionedAsync(snapshot.Process, transition, ct));
        }
    }

    /// <summary>Fires <see cref="IProcessLifecycleObserver.OnTerminatedAsync" /> for every registered observer.</summary>
    public ValueTask NotifyTerminatedAsync(SchemataProcess process, CancellationToken ct) {
        return NotifyAsync(nameof(IProcessLifecycleObserver.OnTerminatedAsync),
                           process,
                           observer => observer.OnTerminatedAsync(process, ct));
    }

    /// <summary>Fires <see cref="IProcessLifecycleObserver.OnFailedAsync" /> for every registered observer.</summary>
    public ValueTask NotifyFailedAsync(
        SchemataProcess   process,
        Exception         exception,
        CancellationToken ct
    ) {
        return NotifyAsync(nameof(IProcessLifecycleObserver.OnFailedAsync),
                           process,
                           observer => observer.OnFailedAsync(process, exception, ct));
    }

    private async ValueTask NotifyAsync(
        string                                operation,
        SchemataProcess                       process,
        Func<IProcessLifecycleObserver, Task> invoke
    ) {
        foreach (var observer in _processObservers) {
            try {
                await invoke(observer);
            } catch (Exception ex) {
                _logger.LogError(ex,
                                 "IProcessLifecycleObserver.{Operation} threw for process '{Name}'.",
                                 operation,
                                 process.CanonicalName);
            }
        }
    }
}
