using System;
using System.Linq;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Shared flow-resolution helpers for the state-machine engine: builds the condition-evaluation
///     context for a token and resolves the outgoing flow from an activity's boundary events. The
///     BPMN engine carries its own resolution in <c>BpmnEngine.ResolveTargetAsync</c>.
/// </summary>
public static class FlowResolver
{
    /// <summary>
    ///     Resolves the first outgoing flow from a boundary event on <paramref name="activity" />
    ///     whose definition matches <paramref name="trigger" />.
    /// </summary>
    public static SequenceFlow? ResolveBoundaryEventFlow(
        ProcessDefinition definition,
        Activity          activity,
        IEventDefinition  trigger
    ) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(trigger);

        var boundaries = definition.Elements.OfType<FlowEvent>()
                                   .Where(e => e.Position == EventPosition.Boundary && e.AttachedTo == activity)
                                   .ToList();

        foreach (var @event in boundaries) {
            if (!FlowEventMatcher.Matches(@event.Definition, trigger)) {
                continue;
            }

            var outgoing = definition.Flows.Where(sf => sf.Source == @event).ToList();
            if (outgoing.Count > 0) {
                return outgoing[0];
            }
        }

        return null;
    }

    /// <summary>
    ///     Builds a condition-evaluation context for the supplied token.
    ///     <paramref name="execution" /> carries the scoped services of the current flow execution,
    ///     shared by condition contexts, task contexts, and advisors.
    /// </summary>
    public static FlowConditionContext BuildConditionContext(
        ProcessDefinition             definition,
        SchemataProcessToken          token,
        string?                       currentStateName,
        FlowExecutionContext          execution
    ) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(execution);

        return new() {
            Definition   = definition,
            Token        = TokenSnapshotFactory.From(token),
            CurrentState = currentStateName ?? string.Empty,
            Execution    = execution,
        };
    }
}
