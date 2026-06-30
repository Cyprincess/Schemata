using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.Bpmn.Runtime.Gateways;

/// <summary>
///     Encapsulates the event-based-gateway trigger dispatch and arrival parking. BpmnEngine
///     delegates to this handler so the event matching, single-route vs parallel-fork decision,
///     and wait-state parking stay out of the engine surface.
/// </summary>
public static class EventBasedGatewayHandler
{
    /// <summary>
    ///     Dispatches a trigger to an event-based gateway: finds the matching catch event
    ///     downstream, then either routes the existing token (exclusive mode) or forks into
    ///     all matched catches (parallel mode).
    /// </summary>
    public static async ValueTask<ProcessSnapshot> TriggerAsync(
        BpmnEngine                engine,
        ProcessDefinition         definition,
        SchemataProcess           process,
        SchemataProcessToken      token,
        List<SchemataProcessToken> working,
        EventBasedGateway         gateway,
        IEventDefinition          trigger,
        FlowExecutionContext      execution
    ) {
        var outgoing = definition.Outgoing(gateway).ToList();
        var matched  = outgoing
                     .Where(f => f.Target is FlowEvent { Position: EventPosition.IntermediateCatch } ev
                              && BpmnEngine.EventMatches(ev.Definition, trigger))
                     .ToList();

        if (matched.Count == 0) {
            throw new InvalidArgumentException(
                SchemataResources.BPMN_INVALID_TRIGGER,
                new Dictionary<string, string?> {
                    ["trigger"] = trigger.Name,
                    ["state"]   = gateway.Name,
                });
        }

        if (gateway.Parallel) {
            return await engine.SpawnFromEventBasedAsync(definition, process, token, working, gateway, matched, trigger, execution);
        }

        return await engine.RouteSingleEventBasedAsync(definition, process, token, working, gateway, matched[0], trigger, execution);
    }

    /// <summary>
    ///     Parks the arriving token at the event-based gateway: sets the token to Waiting,
    ///     records an Advance transition, and recomputes the aggregate state.
    ///     <see cref="TriggerAsync" /> selects matching outgoing flows after a trigger arrives.
    /// </summary>
    public static ProcessSnapshot ArriveAtGateway(
        SchemataProcess            process,
        SchemataProcessToken       token,
        List<SchemataProcessToken> working,
        EventBasedGateway          eb,
        string?                    previousState
    ) {
        var parked = previousState ?? eb.Name;
        var arrivalTransition = BpmnEngine.NewTransition(
            process.Name!,
            token.CanonicalName,
            previousState,
            parked,
            TransitionKind.Move,
            "Advance");

        token.State         = "Waiting";
        token.StateName     = parked;
        token.WaitingAtName = eb.Name;

        BpmnEngine.ApplyAggregateState(process, working);

        return BpmnEngine.Snapshot(process, working, [arrivalTransition]);
    }
}
