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
///     Encapsulates the inclusive-gateway split, merge, and join lifecycle. BpmnEngine delegates
///     to this handler so the condition evaluation, dead-path pruning, and join counting stay
///     out of the engine surface.
/// </summary>
public static class InclusiveGatewayHandler
{
    /// <summary>
    ///     Starts a process whose first flow target is an inclusive gateway: evaluates each
    ///     outgoing condition and spawns one child per matched flow. Throws if no branch
    ///     matches and no default is configured.
    /// </summary>
    public static async ValueTask<ProcessSnapshot> StartIntoBranchAsync(
        BpmnEngine                engine,
        ProcessDefinition         definition,
        SchemataProcess           process,
        InclusiveGateway          ig,
        Dictionary<string, int>     variables,
        FlowExecutionContext      execution
    ) {
        var outgoing = definition.Outgoing(ig).ToList();
        var matched  = await engine.ResolveInclusiveBranchesAsync(definition, ig, outgoing, BpmnEngine.EmptyTokenView(process), variables, execution, process);
        if (matched.Count == 0) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING,
                new Dictionary<string, string?> { ["name"] = ig.Name });
        }

        return await engine.SpawnFromGatewayAsync(definition, process, ig, matched, variables, TransitionKind.Fork, execution);
    }

    /// <summary>
    ///     Branches the active token at an inclusive gateway: evaluates each outgoing condition
    ///     and spawns one child per matched flow. Throws if no branch matches and no default is
    ///     configured.
    /// </summary>
    public static async ValueTask<ProcessSnapshot> BranchFromTokenAsync(
        BpmnEngine                engine,
        ProcessDefinition         definition,
        SchemataProcess           process,
        SchemataProcessToken      token,
        List<SchemataProcessToken> working,
        InclusiveGateway          ig,
        string?                   previousState,
        FlowExecutionContext      execution
    ) {
        var outgoing  = definition.Outgoing(ig).ToList();
        var variables = new Dictionary<string, int>();
        var matched   = await engine.ResolveInclusiveBranchesAsync(definition, ig, outgoing, BpmnEngine.TokenView(token), variables, execution, process, token);
        if (matched.Count == 0) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING,
                new Dictionary<string, string?> { ["name"] = ig.Name });
        }

        return await engine.BranchFromTokenAsync(definition, process, token, working, ig, matched, previousState, TransitionKind.Fork, execution);
    }

    /// <summary>
    ///     Joins an arriving token with the siblings already waiting at the inclusive gateway.
    ///     Parks the token while a live upstream can still reach the gateway; otherwise fires
    ///     the join.
    /// </summary>
    public static async ValueTask<ProcessSnapshot> ArriveAtJoinAsync(
        BpmnEngine                engine,
        ProcessDefinition         definition,
        SchemataProcess           process,
        SchemataProcessToken      token,
        List<SchemataProcessToken> working,
        InclusiveGateway          ig,
        string?                   previousState,
        FlowExecutionContext      execution
    ) {
        var waitingHere  = working
                         .Where(t => !ReferenceEquals(t, token)
                                  && t.StateName == ig.Name
                                  && t.State is { } s
                    && TokenStates.JoinCounted.Contains(s))
                         .ToList();

        var liveUpstream = BpmnEngine.HasLiveUpstreamReachableTo(definition, ig, working, token);

        if (liveUpstream) {
            return engine.ParkAtGateway(process, token, working, ig, previousState);
        }

        return await engine.FireJoinAsync(definition, process, token, working, ig, waitingHere, previousState, execution);
    }
}
