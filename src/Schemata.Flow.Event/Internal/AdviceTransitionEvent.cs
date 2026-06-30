using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Event.Skeleton.Entities;
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
public sealed class AdviceTransitionEvent : IFlowTransitionAdvisor
{
    private readonly IRepository<SchemataEventSubscription> _subscriptions;

    /// <summary>Creates an advisor that persists Flow event subscriptions through the supplied repository.</summary>
    public AdviceTransitionEvent(IRepository<SchemataEventSubscription> subscriptions) {
        _subscriptions = subscriptions;
    }

    #region IFlowTransitionAdvisor Members

    public int Order => 0;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext         ctx,
        FlowTransitionContext context,
        CancellationToken     ct = default
    ) {
        _subscriptions.Join(context.UnitOfWork!);

        var process     = context.Process;
        var instance    = context.Instance;
        var definition  = context.Definition;
        var processName = process.CanonicalName!;

        // The instance carries the new waiting element; PreviousWaitingAtId is the only source for
        // the element being left, so its subscription can be removed.
        if (!string.IsNullOrEmpty(context.PreviousWaitingAtId)
         && context.PreviousWaitingAtId != instance.WaitingAtId
         && definition is not null) {
            var oldElement = definition.Elements.FirstOrDefault(e => e.Id == context.PreviousWaitingAtId);
            foreach (var elementId in ResolveCatchElementIds(oldElement, definition)) {
                await RemoveSubscriptionAsync(SubscriptionId(processName, elementId), ct);
            }
        }

        if (instance.IsComplete || string.IsNullOrEmpty(instance.WaitingAtId) || definition is null) {
            return AdviseResult.Continue;
        }

        var newElement = definition.Elements.FirstOrDefault(e => e.Id == instance.WaitingAtId);
        foreach (var (elementId, eventDef) in ResolveCatchEventDefinitions(newElement, definition)) {
            await UpsertSubscriptionAsync(
                SubscriptionId(processName, elementId),
                eventDef.Name,
                eventDef is Message ? processName : null,
                processName,
                ct);
        }

        return AdviseResult.Continue;
    }

    #endregion

    private async Task RemoveSubscriptionAsync(string subscriptionId, CancellationToken ct) {
        var existing = await _subscriptions.FirstOrDefaultAsync(
            q => q.Where(s => s.SubscriptionId == subscriptionId), ct);
        if (existing is null) {
            return;
        }

        await _subscriptions.RemoveAsync(existing, ct);
    }

    private async Task UpsertSubscriptionAsync(
        string            subscriptionId,
        string            eventType,
        string?           correlationKey,
        string            target,
        CancellationToken ct
    ) {
        var existing = await _subscriptions.FirstOrDefaultAsync(
            q => q.Where(s => s.SubscriptionId == subscriptionId), ct);

        if (existing is null) {
            await _subscriptions.AddAsync(new() {
                Name           = subscriptionId,
                CanonicalName  = $"event-subscriptions/{subscriptionId}",
                SubscriptionId = subscriptionId,
                EventType      = eventType,
                CorrelationKey = correlationKey,
                Target         = target,
            }, ct);
        } else {
            existing.EventType      = eventType;
            existing.CorrelationKey = correlationKey;
            existing.Target         = target;
            await _subscriptions.UpdateAsync(existing, ct);
        }
    }

    private static string SubscriptionId(string processName, string elementId) {
        return $"flow:{processName}:{elementId}";
    }

    private static IEnumerable<string> ResolveCatchElementIds(FlowElement? element, ProcessDefinition definition) {
        return ResolveCatchEventDefinitions(element, definition).Select(t => t.ElementId);
    }

    private static IEnumerable<(string ElementId, IEventDefinition Definition)> ResolveCatchEventDefinitions(
        FlowElement?      element,
        ProcessDefinition definition
    ) {
        if (element is FlowEvent { Position: EventPosition.IntermediateCatch, Definition: not null } evt) {
            yield return (evt.Id, evt.Definition);
        } else if (element is EventBasedGateway gateway) {
            foreach (var flow in definition.Flows.Where(f => f.Source == gateway)) {
                if (flow.Target is FlowEvent {
                    Position: EventPosition.IntermediateCatch, Definition: not null,
                } catchEvt) {
                    yield return (catchEvt.Id, catchEvt.Definition);
                }
            }
        }
    }
}
