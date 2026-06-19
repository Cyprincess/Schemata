using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Event.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;

namespace Schemata.Flow.Event.Internal;

/// <summary>
///     Maintains event-bus subscriptions for BPMN intermediate message and signal catches,
///     including those reached through an event-based gateway. The single-token state machine
///     waits at the host activity for boundary message and signal events. Multi-token engines
///     plugged in via keyed <c>IFlowRuntime</c> may bridge boundary catches by following the
///     intermediate-catch subscription pattern.
/// </summary>
public sealed class FlowEventTransitionAdvisor : IFlowTransitionAdvisor
{
    private readonly IEventSubscriptionStore _store;

    /// <summary>Creates an advisor that stores Flow event subscriptions.</summary>
    public FlowEventTransitionAdvisor(IEventSubscriptionStore store) {
        _store = store;
    }

    #region IFlowTransitionAdvisor Members

    public int Order => 0;

    public async Task<AdviseResult> AdviseAsync(AdviceContext ctx, FlowTransitionContext context, CancellationToken ct = default) {
        var process    = context.Process;
        var instance   = context.Instance;
        var definition = context.Definition;

        // The instance carries the new waiting element; PreviousWaitingAtId is the only source for
        // the element being left, so its subscription can be removed.
        if (!string.IsNullOrEmpty(context.PreviousWaitingAtId)
         && context.PreviousWaitingAtId != instance.WaitingAtId
         && definition is not null) {
            var processName = process.CanonicalName!;
            var oldElement  = definition.Elements.FirstOrDefault(e => e.Id == context.PreviousWaitingAtId);
            await RemoveSubscriptionsAsync(oldElement, definition, processName, ct);
        }

        if (instance.IsComplete) {
            return AdviseResult.Continue;
        }

        if (string.IsNullOrEmpty(instance.WaitingAtId) || definition is null) {
            return AdviseResult.Continue;
        }

        var element = definition.Elements.FirstOrDefault(e => e.Id == instance.WaitingAtId);

        if (element is FlowEvent { Position: EventPosition.IntermediateCatch, Definition: not null } evt) {
            await AddSubscriptionAsync(process, evt.Id, evt.Definition, ct);
        } else if (element is EventBasedGateway gateway) {
            var outgoing = definition.Flows.Where(f => f.Source == gateway);
            foreach (var flow in outgoing) {
                if (flow.Target is FlowEvent {
                    Position: EventPosition.IntermediateCatch, Definition: not null,
                } catchEvt) {
                    await AddSubscriptionAsync(process, catchEvt.Id, catchEvt.Definition, ct);
                }
            }
        }

        return AdviseResult.Continue;
    }

    #endregion

    private async Task RemoveSubscriptionsAsync(
        FlowElement?      element,
        ProcessDefinition definition,
        string            processName,
        CancellationToken ct
    ) {
        if (element is FlowEvent { Definition: not null } evt) {
            var id = $"flow:{processName}:{evt.Id}";
            await _store.RemoveAsync(id, ct);
        } else if (element is EventBasedGateway gateway) {
            var outgoing = definition.Flows.Where(f => f.Source == gateway);
            foreach (var flow in outgoing) {
                if (flow.Target is FlowEvent {
                    Position: EventPosition.IntermediateCatch, Definition: not null,
                } catchEvt) {
                    var id = $"flow:{processName}:{catchEvt.Id}";
                    await _store.RemoveAsync(id, ct);
                }
            }
        }
    }

    private async Task AddSubscriptionAsync(
        SchemataProcess   process,
        string            elementId,
        IEventDefinition  definition,
        CancellationToken ct
    ) {
        var correlationKey = definition is Message ? process.CanonicalName : null;
        // Key the subscription by the waiting element id so two catches sharing an event
        // name in one process keep distinct subscriptions.
        var id             = $"flow:{process.CanonicalName}:{elementId}";
        var target         = process.CanonicalName!;
        var eventType      = definition.Name;

        var subscription = new EventSubscription(id, eventType, correlationKey, target);
        await _store.AddAsync(subscription, ct);
    }
}
