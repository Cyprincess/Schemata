using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.Bpmn.Runtime.Gateways;

/// <summary>
///     Encapsulates the parallel-gateway fork and join lifecycle. BpmnEngine delegates to this
///     handler so the fan-out / fan-in mechanics stay out of the engine surface.
/// </summary>
public static class ParallelGatewayHandler
{
    /// <summary>
    ///     Starts a process whose first flow target is a parallel fork: splits the incoming
    ///     token into one child per outgoing flow and records a Fork transition row.
    /// </summary>
    public static async ValueTask<ProcessSnapshot> StartIntoForkAsync(
        BpmnEngine                engine,
        ProcessDefinition         definition,
        SchemataProcess           process,
        ParallelGateway           pg,
        Dictionary<string, int>     variables,
        FlowExecutionContext      execution
    ) {
        var outgoing = definition.Outgoing(pg).ToList();
        return await engine.SpawnFromGatewayAsync(definition, process, pg, outgoing, variables, TransitionKind.Fork, execution);
    }

    /// <summary>
    ///     Forks the active token at a parallel gateway: completes the arriving token, records a
    ///     Fork transition, and spawns one child per outgoing flow.
    /// </summary>
    public static async ValueTask<ProcessSnapshot> ForkFromTokenAsync(
        BpmnEngine                engine,
        ProcessDefinition         definition,
        SchemataProcess           process,
        SchemataProcessToken      token,
        List<SchemataProcessToken> working,
        ParallelGateway           pg,
        string?                   previousState,
        FlowExecutionContext      execution
    ) {
        var outgoing = definition.Outgoing(pg).ToList();
        return await engine.BranchFromTokenAsync(definition, process, token, working, pg, outgoing, previousState, TransitionKind.Fork, execution);
    }

    /// <summary>
    ///     Joins an arriving token with the siblings already waiting at the parallel gateway.
    ///     Parks the token if not all incoming tokens have arrived; otherwise fires the join.
    /// </summary>
    public static async ValueTask<ProcessSnapshot> ArriveAtJoinAsync(
        BpmnEngine                engine,
        ProcessDefinition         definition,
        SchemataProcess           process,
        SchemataProcessToken      token,
        List<SchemataProcessToken> working,
        ParallelGateway           pg,
        string?                   previousState,
        FlowExecutionContext      execution
    ) {
        var incomingCount = definition.Incoming(pg).Count;
        var waitingHere   = working
                          .Where(t => !ReferenceEquals(t, token)
                                   && t.StateName == pg.Name
                                   && t.State is { } s
                    && TokenStates.JoinCounted.Contains(s))
                          .ToList();

        if (waitingHere.Count + 1 < incomingCount) {
            return engine.ParkAtGateway(process, token, working, pg, previousState, execution);
        }

        return await engine.FireJoinAsync(definition, process, token, working, pg, waitingHere, previousState, execution);
    }
}
