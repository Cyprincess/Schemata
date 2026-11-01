using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Bpmn.Runtime.Boundary;

/// <summary>
///     Creates the spawn transition for a staged non-interrupting boundary branch.
/// </summary>
/// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
/// <typeparam name="TTransition">The persisted transition entity type registered by the host.</typeparam>
public sealed class NonInterruptingBoundaryHandler<TProcess, TToken, TTransition>
    where TProcess : SchemataProcess, new()
    where TToken : SchemataProcessToken, new()
    where TTransition : SchemataProcessTransition, new()
{
    /// <summary>
    ///     Creates the boundary branch's spawn transition.
    /// </summary>
    internal ValueTask<SchemataProcessTransition> HandleAsync(
        SchemataProcess            process,
        SchemataProcessToken       spawned,
        FlowEvent                  boundary,
        TargetState     resolved,
        IEventDefinition           trigger
    ) {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(spawned);
        ArgumentNullException.ThrowIfNull(boundary);
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(trigger);

        return ValueTask.FromResult<SchemataProcessTransition>(BpmnEngine<TProcess, TToken, TTransition>.NewTransition(
            process.Name!,
            spawned.CanonicalName,
            boundary.Name,
            resolved.StateName,
            TransitionKind.Spawn,
            trigger.Name));
    }
}
