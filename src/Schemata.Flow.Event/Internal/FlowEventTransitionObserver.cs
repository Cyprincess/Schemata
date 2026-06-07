using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Event.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;

namespace Schemata.Flow.Event.Internal;

/// <summary>Maintains event-bus subscriptions for BPMN intermediate message and signal catches.</summary>
public sealed class FlowEventTransitionObserver : IFlowTransitionObserver
{
    private readonly IEventSubscriptionStore _store;

    public FlowEventTransitionObserver(IEventSubscriptionStore store) {
        _store = store;
    }

    #region IFlowTransitionObserver Members

    public async Task OnTransitionedAsync(FlowTransitionContext context, CancellationToken ct = default) {
        var process    = context.Process;
        var instance   = context.Instance;
        var definition = context.Definition;

        // process.WaitingAtId already equals instance.WaitingAtId here; PreviousWaitingAtId
        // is the only source for the pre-transition value.
        if (!string.IsNullOrEmpty(context.PreviousWaitingAtId)
         && context.PreviousWaitingAtId != instance.WaitingAtId
         && definition is not null) {
            var processName = process.CanonicalName!;
            var oldElement  = definition.Elements.FirstOrDefault(e => e.Id == context.PreviousWaitingAtId);
            await RemoveSubscriptionsAsync(oldElement, definition, processName, ct);
        }

        if (instance.IsComplete) {
            return;
        }

        if (string.IsNullOrEmpty(instance.WaitingAtId) || definition is null) {
            return;
        }

        var element = definition.Elements.FirstOrDefault(e => e.Id == instance.WaitingAtId);

        if (element is FlowEvent { Position: EventPosition.IntermediateCatch, Definition: not null } evt) {
            await AddSubscriptionAsync(process, evt.Definition, ct);
        } else if (element is EventBasedGateway gateway) {
            var outgoing = definition.Flows.Where(f => f.Source == gateway);
            foreach (var flow in outgoing) {
                if (flow.Target is FlowEvent {
                    Position: EventPosition.IntermediateCatch, Definition: not null,
                } catchEvt) {
                    await AddSubscriptionAsync(process, catchEvt.Definition, ct);
                }
            }
        }
    }

    #endregion

    private async Task RemoveSubscriptionsAsync(
        FlowElement?      element,
        ProcessDefinition definition,
        string            processName,
        CancellationToken ct
    ) {
        if (element is FlowEvent { Definition: not null } evt) {
            var id = $"flow:{processName}:{evt.Definition.Name}";
            await _store.RemoveAsync(id, ct);
        } else if (element is EventBasedGateway gateway) {
            var outgoing = definition.Flows.Where(f => f.Source == gateway);
            foreach (var flow in outgoing) {
                if (flow.Target is FlowEvent {
                    Position: EventPosition.IntermediateCatch, Definition: not null,
                } catchEvt) {
                    var id = $"flow:{processName}:{catchEvt.Definition.Name}";
                    await _store.RemoveAsync(id, ct);
                }
            }
        }
    }

    private async Task AddSubscriptionAsync(
        SchemataProcess   process,
        IEventDefinition  definition,
        CancellationToken ct
    ) {
        var correlationKey = definition is Message ? process.CanonicalName : null;
        var id             = $"flow:{process.CanonicalName}:{definition.Name}";
        var target         = process.CanonicalName!;
        var eventType      = definition.Name;

        var subscription = new EventSubscription(id, eventType, correlationKey, target);
        await _store.AddAsync(subscription, ct);
    }
}
