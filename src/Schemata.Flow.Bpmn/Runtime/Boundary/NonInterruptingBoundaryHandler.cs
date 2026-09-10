using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Bpmn.Runtime.Boundary;

/// <summary>
///     Handles BPMN non-interrupting boundary events by spawning a sibling token on the boundary
///     branch while leaving the attached host token unchanged.
/// </summary>
public sealed class NonInterruptingBoundaryHandler
{
    /// <summary>
    ///     Spawns a boundary-branch sibling token and returns the corresponding spawn transition.
    /// </summary>
    internal async ValueTask<SchemataProcessTransition> HandleAsync(
        SchemataProcess            process,
        SchemataProcessToken       hostToken,
        List<SchemataProcessToken> working,
        FlowEvent                  boundary,
        TargetState     resolved,
        IEventDefinition           trigger,
        FlowExecutionContext       execution
    ) {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(hostToken);
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(boundary);
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(trigger);

        var spawned = BpmnEngine.NewChildToken(process, resolved, hostToken);
        await execution.CreateTokenAsync(spawned, CancellationToken.None);
        working.Add(spawned);

        return BpmnEngine.NewTransition(
            process.Name!,
            spawned.CanonicalName,
            boundary.Name,
            resolved.StateName,
            TransitionKind.Spawn,
            trigger.Name);
    }
}
