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
///     Concrete helper that fans process and token lifecycle events out to every registered
///     <see cref="IProcessLifecycleObserver" /> / <see cref="ITokenLifecycleObserver" />. Observer
///     exceptions are caught and logged at <see cref="LogLevel.Warning" />; they never abort a
///     transition. Token-level events for fork / join are intentionally left for the BPMN engine to
///     fire directly — the state-machine engine only produces <see cref="TransitionKind.Move" /> /
///     <see cref="TransitionKind.Cancel" /> / <see cref="TransitionKind.Fail" /> rows, which this
///     helper turns into the matching token observer calls.
/// </summary>
public class ProcessLifecycleNotifier
{
    private readonly ILogger<ProcessLifecycleNotifier> _logger;
    private readonly IList<IProcessLifecycleObserver>  _processObservers;
    private readonly IList<ITokenLifecycleObserver>    _tokenObservers;

    /// <summary>Creates a notifier bound to the resolved observer lists.</summary>
    public ProcessLifecycleNotifier(
        IEnumerable<IProcessLifecycleObserver> processObservers,
        IEnumerable<ITokenLifecycleObserver>   tokenObservers,
        ILogger<ProcessLifecycleNotifier>      logger
    ) {
        _processObservers = processObservers.ToList();
        _tokenObservers   = tokenObservers.ToList();
        _logger           = logger;
    }

    /// <summary>Fires <see cref="IProcessLifecycleObserver.OnStartedAsync" /> for every registered observer.</summary>
    public virtual async ValueTask NotifyStartedAsync(ProcessSnapshot snapshot, CancellationToken ct) {
        var process = snapshot.Process;
        foreach (var observer in _processObservers) {
            try {
                await observer.OnStartedAsync(process, ct);
            } catch (Exception ex) {
                _logger.LogWarning(ex,
                                   "IProcessLifecycleObserver.OnStartedAsync threw for process '{Name}'.",
                                   process.CanonicalName);
            }
        }
    }

    /// <summary>
    ///     Fires <see cref="IProcessLifecycleObserver.OnTransitionedAsync" /> for every transition
    ///     in <paramref name="snapshot" />, then derives matching token-level events for the BPMN /
    ///     state-machine engines.
    /// </summary>
    public virtual async ValueTask NotifyTransitionedAsync(ProcessSnapshot snapshot, CancellationToken ct) {
        var process = snapshot.Process;
        foreach (var transition in snapshot.Transitions) {
            foreach (var observer in _processObservers) {
                try {
                    await observer.OnTransitionedAsync(process, transition, ct);
                } catch (Exception ex) {
                    _logger.LogWarning(ex,
                                       "IProcessLifecycleObserver.OnTransitionedAsync threw for process '{Name}'.",
                                       process.CanonicalName);
                }
            }

            if (transition.Kind is TransitionKind.Fail or TransitionKind.Cancel) {
                await NotifyTokenLifecycleAsync(snapshot, transition, ct);
            }
        }
    }

    /// <summary>Fires <see cref="IProcessLifecycleObserver.OnTerminatedAsync" /> for every registered observer.</summary>
    public virtual async ValueTask NotifyTerminatedAsync(SchemataProcess process, CancellationToken ct) {
        foreach (var observer in _processObservers) {
            try {
                await observer.OnTerminatedAsync(process, ct);
            } catch (Exception ex) {
                _logger.LogWarning(ex,
                                   "IProcessLifecycleObserver.OnTerminatedAsync threw for process '{Name}'.",
                                   process.CanonicalName);
            }
        }
    }

    /// <summary>Fires <see cref="IProcessLifecycleObserver.OnFailedAsync" /> for every registered observer.</summary>
    public virtual async ValueTask NotifyFailedAsync(
        SchemataProcess   process,
        Exception         exception,
        CancellationToken ct
    ) {
        foreach (var observer in _processObservers) {
            try {
                await observer.OnFailedAsync(process, exception, ct);
            } catch (Exception ex) {
                _logger.LogWarning(ex,
                                   "IProcessLifecycleObserver.OnFailedAsync threw for process '{Name}'.",
                                   process.CanonicalName);
            }
        }
    }

    private async ValueTask NotifyTokenLifecycleAsync(
        ProcessSnapshot           snapshot,
        SchemataProcessTransition transition,
        CancellationToken         ct
    ) {
        if (_tokenObservers.Count == 0) {
            return;
        }

        var token = snapshot.Tokens.FirstOrDefault(t => t.CanonicalName == transition.Token);
        if (token is null) {
            return;
        }

        var process = snapshot.Process;
        var view    = TokenSnapshotFactory.From(token);

        foreach (var observer in _tokenObservers) {
            try {
                switch (transition.Kind) {
                    case TransitionKind.Cancel:
                        await observer.OnTokenCancelledAsync(process, view, ct);
                        break;
                    case TransitionKind.Fail:
                        await observer.OnTokenFailedAsync(process, view, new(transition.Note ?? "token failed"), ct);
                        break;
                }
            } catch (Exception ex) {
                _logger.LogWarning(ex,
                                   "ITokenLifecycleObserver threw for token '{Token}' on process '{Name}'.",
                                   token.CanonicalName,
                                   process.CanonicalName);
            }
        }
    }
}
