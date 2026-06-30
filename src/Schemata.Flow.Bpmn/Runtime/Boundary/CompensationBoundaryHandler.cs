using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Flow.Bpmn.Runtime.Compensation;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.Bpmn.Runtime.Boundary;

/// <summary>Recognizes BPMN compensation boundary events and registers deferred compensation handlers.</summary>
public static class CompensationBoundaryHandler
{
    /// <summary>Finds all compensation boundary events attached to a host activity.</summary>
    /// <param name="definition">The process definition that owns the boundary events.</param>
    /// <param name="host">The activity whose attached boundaries are inspected.</param>
    /// <returns>Compensation boundary events in definition traversal order.</returns>
    public static IEnumerable<FlowEvent> FindCompensationBoundaries(ProcessDefinition definition, Activity host) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(host);

        return definition.AllElements
                        .OfType<FlowEvent>()
                        .Where(e => e.Position == EventPosition.Boundary
                                 && e.AttachedTo == host
                                 && e.Definition is CompensationDefinition);
    }

    /// <summary>Builds a deferred handler for a compensation boundary event.</summary>
    /// <param name="definition">The process definition that owns the boundary event.</param>
    /// <param name="host">The completed activity that registered the boundary handler.</param>
    /// <param name="boundary">The compensation boundary event.</param>
    /// <returns>A handler for the boundary's outgoing target, or <see langword="null" /> when the boundary has no outgoing flow.</returns>
    public static ICompensationHandler? Build(ProcessDefinition definition, Activity host, FlowEvent boundary) {
        return Build(definition, host, boundary, null);
    }

    /// <summary>Builds a deferred handler for a compensation boundary event with an execution facade.</summary>
    /// <param name="definition">The process definition that owns the boundary event.</param>
    /// <param name="host">The completed activity that registered the boundary handler.</param>
    /// <param name="boundary">The compensation boundary event.</param>
    /// <param name="executor">The runtime facade used when the handler is invoked.</param>
    /// <returns>A handler for the boundary's outgoing target, or <see langword="null" /> when the boundary has no outgoing flow.</returns>
    public static ICompensationHandler? Build(
        ProcessDefinition      definition,
        Activity               host,
        FlowEvent              boundary,
        ICompensationExecutor? executor) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(boundary);

        var outgoing = definition.FirstOutgoing(boundary);
        return outgoing is null
            ? null
            : new BoundaryCompensationHandler(host, outgoing.Target, boundary.Name, executor);
    }

    /// <summary>Registers every compensation boundary attached to a host activity into the supplied stack.</summary>
    /// <param name="definition">The process definition that owns the boundary events.</param>
    /// <param name="host">The completed activity whose boundaries are registered.</param>
    /// <param name="stack">The scope-local compensation stack receiving handlers.</param>
    public static void RegisterAll(ProcessDefinition definition, Activity host, CompensationStack stack) {
        RegisterAll(definition, host, stack, null);
    }

    /// <summary>Registers every compensation boundary attached to a host activity into the supplied stack.</summary>
    /// <param name="definition">The process definition that owns the boundary events.</param>
    /// <param name="host">The completed activity whose boundaries are registered.</param>
    /// <param name="stack">The scope-local compensation stack receiving handlers.</param>
    /// <param name="executor">The runtime facade used by each registered handler.</param>
    public static void RegisterAll(
        ProcessDefinition      definition,
        Activity               host,
        CompensationStack      stack,
        ICompensationExecutor? executor) {
        ArgumentNullException.ThrowIfNull(stack);

        foreach (var boundary in FindCompensationBoundaries(definition, host)) {
            var handler = Build(definition, host, boundary, executor);
            if (handler is not null) {
                stack.Register(handler);
            }
        }
    }
}
