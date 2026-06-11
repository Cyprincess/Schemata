using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Event.Skeleton;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Event.Internal;

/// <summary>Dispatches inbound events to waiting BPMN message or signal catches.</summary>
public sealed class FlowEventHandler : IEventHandler<IEvent>
{
    private readonly IEventDispatchContext _context;
    private readonly IProcessRuntime       _runtime;

    public FlowEventHandler(IProcessRuntime runtime, IEventDispatchContext context) {
        _runtime = runtime;
        _context = context;
    }

    #region IEventHandler<IEvent> Members

    public async Task HandleAsync(IEvent @event, CancellationToken ct) {
        var subs = _context.MatchedSubscriptions;
        if (subs == null || subs.Count == 0) return;

        var signals = new HashSet<string>();
        foreach (var sub in subs) {
            if (string.IsNullOrEmpty(sub.Target)) continue;

            if (sub.CorrelationKey != null) {
                await _runtime.CorrelateMessageAsync(sub.Target, sub.EventType, @event, ct: ct);
            } else if (signals.Add(sub.EventType)) {
                await _runtime.ThrowSignalAsync(sub.EventType, @event, ct: ct);
            }
        }
    }

    #endregion
}
