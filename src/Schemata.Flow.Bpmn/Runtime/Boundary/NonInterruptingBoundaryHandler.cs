using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Bpmn.Runtime.Boundary;

/// <summary>
///     Records the staged sibling token for a non-interrupting boundary branch while preserving
///     the attached host token.
/// </summary>
public sealed class NonInterruptingBoundaryHandler
{
    /// <summary>
    ///     Records the boundary-branch sibling and its spawn transition.
    /// </summary>
    internal ValueTask<SchemataProcessTransition> HandleAsync(
        SchemataProcess            process,
        SchemataProcessToken       spawned,
        List<SchemataProcessToken> working,
        FlowEvent                  boundary,
        TargetState     resolved,
        IEventDefinition           trigger
    ) {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(spawned);
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(boundary);
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(trigger);

        working.Add(spawned);

        return ValueTask.FromResult(BpmnEngine.NewTransition(
            process.Name!,
            spawned.CanonicalName,
            boundary.Name,
            resolved.StateName,
            TransitionKind.Spawn,
            trigger.Name));
    }
}
