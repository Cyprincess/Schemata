using System;
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
///     including those reached through an event-based gateway, and for boundary message and
///     signal catches attached to the activity hosting the active token. The single-token state
///     machine waits at the host activity for boundary message and signal events, so boundary
///     subscriptions follow the token's active state (<see cref="TokenSnapshot.WaitingAtName" />
///     is empty) rather than a waiting element. Multi-token engines plugged in via keyed
///     <c>IFlowRuntime</c> may bridge boundary catches by following the intermediate-catch
///     subscription pattern.
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

        var token       = context.Token;
        var definition  = context.Definition;
        var processName = context.Snapshot.Process.CanonicalName!;

        if (definition is not null) {
            // PreviousWaitingAtName is the only source for the waiting element being left, so its
            // subscription can be removed when the token moved off it.
            if (!string.IsNullOrEmpty(context.PreviousWaitingAtName)
             && context.PreviousWaitingAtName != token.WaitingAtName) {
                var oldElement = definition.AllElements.FirstOrDefault(e => e.Name == context.PreviousWaitingAtName);
                foreach (var elementName in ResolveCatchElementNames(oldElement, definition)) {
                    await RemoveSubscriptionAsync(SubscriptionId(processName, elementName, token.CanonicalName), ct);
                }
            }

            // Boundary subscriptions follow the host activity rather than a waiting element, so
            // they are removed when the token leaves the host — by completion, boundary fire, or
            // termination. The previous state comes from the transition row, since the token rows
            // in the snapshot already carry the new state.
            var previousState = PreviousStateOf(context);
            if (!string.IsNullOrEmpty(previousState)
             && previousState != token.StateName
             && definition.AllElements.FirstOrDefault(e => e.Name == previousState) is Activity previousHost) {
                foreach (var (elementName, _) in ResolveBoundaryCatchEventDefinitions(previousHost, definition)) {
                    await RemoveSubscriptionAsync(SubscriptionId(processName, elementName, token.CanonicalName), ct);
                }
            }
        }

        if (definition is null) {
            return AdviseResult.Continue;
        }

        if (!string.IsNullOrEmpty(token.WaitingAtName)) {
            var newElement = definition.AllElements.FirstOrDefault(e => e.Name == token.WaitingAtName);
            foreach (var (elementName, eventDef) in ResolveCatchEventDefinitions(newElement, definition)) {
                var subscriptionToken = eventDef is Message ? token.CanonicalName : null;
                await UpsertSubscriptionAsync(
                    SubscriptionId(processName, elementName, subscriptionToken),
                    eventDef.Name,
                    eventDef is Message ? processName : null,
                    processName,
                    subscriptionToken,
                    ct);
            }

            return AdviseResult.Continue;
        }

        // An active token parked on a host activity has no waiting element, but its boundary
        // message/signal catches are live: arm them so inbound events can be routed here.
        if (string.Equals(token.Status, "Active", StringComparison.Ordinal)
         && definition.AllElements.FirstOrDefault(e => e.Name == token.StateName) is Activity host) {
            foreach (var (elementName, eventDef) in ResolveBoundaryCatchEventDefinitions(host, definition)) {
                var subscriptionToken = eventDef is Message ? token.CanonicalName : null;
                await UpsertSubscriptionAsync(
                    SubscriptionId(processName, elementName, subscriptionToken),
                    eventDef.Name,
                    eventDef is Message ? processName : null,
                    processName,
                    subscriptionToken,
                    ct);
            }
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
        string?           token,
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
                Token          = token,
            }, ct);
        } else {
            existing.EventType      = eventType;
            existing.CorrelationKey = correlationKey;
            existing.Target         = target;
            existing.Token          = token;
            await _subscriptions.UpdateAsync(existing, ct);
        }
    }

    private static string SubscriptionId(string processName, string elementName, string? token) {
        return $"flow:{processName}:{elementName}:{token ?? "broadcast"}";
    }

    private static string? PreviousStateOf(FlowTransitionContext context) {
        return context.Snapshot.Transitions
                      .Where(transition => transition.Token == context.Token.CanonicalName)
                      .Select(transition => transition.Previous)
                      .FirstOrDefault();
    }

    private static IEnumerable<string> ResolveCatchElementNames(FlowElement? element, ProcessDefinition definition) {
        return ResolveCatchEventDefinitions(element, definition).Select(t => t.ElementName);
    }

    private static IEnumerable<(string ElementName, IEventDefinition Definition)> ResolveBoundaryCatchEventDefinitions(
        Activity          host,
        ProcessDefinition definition
    ) {
        foreach (var evt in definition.AllElements.OfType<FlowEvent>()) {
            if (evt is not { Position: EventPosition.Boundary, Definition: Message or Signal }) {
                continue;
            }

            if (!ReferenceEquals(evt.AttachedTo, host)) {
                continue;
            }

            yield return (evt.Name, evt.Definition);
        }
    }

    private static IEnumerable<(string ElementName, IEventDefinition Definition)> ResolveCatchEventDefinitions(
        FlowElement?      element,
        ProcessDefinition definition
    ) {
        if (element is FlowEvent { Position: EventPosition.IntermediateCatch, Definition: not null } evt) {
            yield return (evt.Name, evt.Definition);
        } else if (element is EventBasedGateway gateway) {
            foreach (var flow in definition.Flows.Where(f => f.Source == gateway)) {
                if (flow.Target is FlowEvent {
                    Position: EventPosition.IntermediateCatch, Definition: not null,
                } catchEvt) {
                    yield return (catchEvt.Name, catchEvt.Definition);
                }
            }
        }
    }
}
