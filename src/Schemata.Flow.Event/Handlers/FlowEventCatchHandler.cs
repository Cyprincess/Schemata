using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Event.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Event.Handlers;

/// <summary>
///     Delivers message and signal catches by correlating them through the event bus. Maintains the
///     <see cref="SchemataEventSubscription" /> rows for BPMN intermediate message and signal catches,
///     including those reached through an event-based gateway, and for boundary message and signal
///     catches attached to the activity hosting the active token.
/// </summary>
/// <remarks>
///     The single-token state machine waits at the host activity for boundary message and signal
///     events, so boundary subscriptions follow the token's active state (<see cref="TokenSnapshot.WaitingAtName" />
///     is empty) rather than a waiting element. Multi-token engines plugged in via keyed
///     <c>IFlowRuntime</c> may bridge boundary catches by following the intermediate-catch
///     subscription pattern.
/// </remarks>
public sealed class FlowEventCatchHandler : IFlowCatchHandler
{

    #region IFlowCatchHandler Members

    public bool Handles(FlowCatchKind kind) {
        return kind is FlowCatchKind.Message or FlowCatchKind.Signal;
    }

    public async ValueTask ArmAsync(FlowTransitionContext context, CancellationToken ct = default) {
        if (context.UnitOfWork is null) {
            throw new FailedPreconditionException(
                SchemataResources.FLOW_EVENT_SUBSCRIPTION_REQUIRES_UNIT_OF_WORK,
                new Dictionary<string, string?> { ["token"] = context.Token.CanonicalName });
        }

        var services = context.Execution.Services;
        var subscriptions = services.GetRequiredService<IRepository<SchemataEventSubscription>>();
        var mutations = services.GetRequiredService<IResourceMutation<SchemataEventSubscription>>();
        subscriptions.Join(context.UnitOfWork);

        var token       = context.Token;
        var definition  = context.Definition;
        var processName = context.Snapshot.Process.CanonicalName!;

        if (definition is null) {
            return;
        }

        // PreviousWaitingAtName is the only source for the waiting element being left, so its
        // subscription can be removed when the token moved off it.
        if (!string.IsNullOrEmpty(context.PreviousWaitingAtName)
         && context.PreviousWaitingAtName != token.WaitingAtName) {
            var oldElement = definition.AllElements.FirstOrDefault(e => e.Name == context.PreviousWaitingAtName);
            foreach (var elementName in ResolveCatchElementNames(oldElement, definition)) {
                await RemoveSubscriptionAsync(subscriptions, mutations, SubscriptionId(processName, elementName, token.CanonicalName), context.UnitOfWork, ct);
            }
        }

        // Boundary subscriptions follow the host activity rather than a waiting element, so they are
        // removed when the token leaves the host — by completion, boundary fire, or termination. The
        // previous state comes from the transition row, since the token rows in the snapshot already
        // carry the new state.
        var previousState = PreviousStateOf(context);
        if (!string.IsNullOrEmpty(previousState)
         && previousState != token.StateName
         && definition.AllElements.FirstOrDefault(e => e.Name == previousState) is Activity previousHost) {
            foreach (var (elementName, _) in ResolveBoundaryCatchEventDefinitions(previousHost, definition)) {
                await RemoveSubscriptionAsync(subscriptions, mutations, SubscriptionId(processName, elementName, token.CanonicalName), context.UnitOfWork, ct);
            }
        }

        if (!string.IsNullOrEmpty(token.WaitingAtName)) {
            var newElement = definition.AllElements.FirstOrDefault(e => e.Name == token.WaitingAtName);
            foreach (var (elementName, eventDef) in ResolveCatchEventDefinitions(newElement, definition)) {
                await UpsertAsync(subscriptions, mutations, processName, elementName, eventDef, token.CanonicalName, context.UnitOfWork, ct);
            }

            return;
        }

        // An active token parked on a host activity has no waiting element, but its boundary
        // message/signal catches are live: arm them so inbound events can be routed here.
        if (string.Equals(token.Status, "Active", StringComparison.Ordinal)
         && definition.AllElements.FirstOrDefault(e => e.Name == token.StateName) is Activity host) {
            foreach (var (elementName, eventDef) in ResolveBoundaryCatchEventDefinitions(host, definition)) {
                await UpsertAsync(subscriptions, mutations, processName, elementName, eventDef, token.CanonicalName, context.UnitOfWork, ct);
            }
        }
    }

    #endregion

    private static async Task UpsertAsync(
        IRepository<SchemataEventSubscription> subscriptions,
        IResourceMutation<SchemataEventSubscription> mutations,
        string            processName,
        string            elementName,
        IEventDefinition  eventDef,
        string            tokenCanonical,
        IUnitOfWork       unitOfWork,
        CancellationToken ct
    ) {
        // Messages correlate to one token; signals stay process-level broadcasts.
        var subscriptionToken = eventDef is Message ? tokenCanonical : null;
        await UpsertSubscriptionAsync(
            subscriptions, mutations,
            SubscriptionId(processName, elementName, subscriptionToken),
            eventDef.Name,
            eventDef is Message ? processName : null,
            processName,
            subscriptionToken,
            unitOfWork,
            ct);
    }

    private static async Task RemoveSubscriptionAsync(IRepository<SchemataEventSubscription> subscriptions,
        IResourceMutation<SchemataEventSubscription> mutations, string subscriptionId, IUnitOfWork unitOfWork, CancellationToken ct) {
        var existing = await subscriptions.FirstOrDefaultAsync(
            q => q.Where(s => s.SubscriptionId == subscriptionId), ct);
        if (existing is null) {
            return;
        }

        await mutations.DeleteAsync(existing, unitOfWork, ct: ct);
    }

    private static async Task UpsertSubscriptionAsync(
        IRepository<SchemataEventSubscription> subscriptions,
        IResourceMutation<SchemataEventSubscription> mutations,
        string            subscriptionId,
        string            eventType,
        string?           correlationKey,
        string            target,
        string?           token,
        IUnitOfWork       unitOfWork,
        CancellationToken ct
    ) {
        var existing = await subscriptions.FirstOrDefaultAsync(
            q => q.Where(s => s.SubscriptionId == subscriptionId), ct);

        if (existing is null) {
            await mutations.CreateAsync(new() {
                SubscriptionId = subscriptionId,
                EventType      = eventType,
                CorrelationKey = correlationKey,
                Target         = target,
                Token          = token,
            }, unitOfWork, ct);
        } else {
            existing.EventType      = eventType;
            existing.CorrelationKey = correlationKey;
            existing.Target         = target;
            existing.Token          = token;
            await mutations.UpdateAsync(existing, unitOfWork, ct: ct);
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
