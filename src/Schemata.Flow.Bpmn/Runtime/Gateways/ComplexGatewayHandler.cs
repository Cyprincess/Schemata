using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Utilities;
using static Schemata.Abstractions.SchemataResources;

namespace Schemata.Flow.Bpmn.Runtime.Gateways;

/// <summary>
///     Executes the BPMN complex-gateway split and merge lifecycle. The optional activation
///     count is a readiness condition: while it evaluates false the arriving token parks at
///     the gateway, and once it passes (or when none is configured) the split resolves through
///     the inclusive-gateway branch logic.
/// </summary>
public static class ComplexGatewayHandler
{
    /// <summary>
    ///     Branches the active token at a complex gateway by reusing the inclusive-gateway
    ///     condition evaluation and split logic. A configured activation count is evaluated
    ///     first; the token parks at the gateway until it passes.
    /// </summary>
    public static async ValueTask<ProcessSnapshot> FromTokenAsync(
        BpmnEngine                engine,
        ProcessDefinition         definition,
        SchemataProcess           process,
        SchemataProcessToken      token,
        List<SchemataProcessToken> working,
        ComplexGateway            cg,
        string?                   previousState,
        FlowExecutionContext      execution
    ) {
        if (cg.ActivationCount is not null) {
            var bookkeeping = token.Bookkeeping;
            var ctx         = engine.BuildConditionContext(definition, BpmnEngine.TokenView(token), cg.Name, execution, bookkeeping, process, token);
            var ready = await cg.ActivationCount.Evaluate(ctx);
            if (!ready) {
                return engine.ParkAtGateway(process, token, working, cg, previousState);
            }
        }

        var outgoing  = definition.Outgoing(cg).ToList();
        var variables = new Dictionary<string, int>();
        var matched   = await engine.ResolveInclusiveBranchesAsync(
            definition,
            cg,
            outgoing,
            BpmnEngine.TokenView(token),
            variables,
            execution,
            process,
            token);

        if (matched.Count == 0) {
            throw new FailedPreconditionException(
                STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING,
                new Dictionary<string, string?> { ["name"] = cg.Name });
        }

        return await engine.BranchFromTokenAsync(
            definition, process, token, working, cg, matched, previousState, TransitionKind.Fork, execution);
    }
}
