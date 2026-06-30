using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Models;
using static Schemata.Abstractions.SchemataResources;

namespace Schemata.Flow.Skeleton.Utilities;

/// <summary>
///     Convenience accessors over <see cref="ProcessDefinition" />'s graph indexes so
///     engine code does not need to repeat <c>TryGetValue(... , out var l) ? l : []</c> at every
///     call site.
/// </summary>
public static class ProcessDefinitionExtensions
{
    /// <summary>
    ///     Returns the sequence flows that originate from <paramref name="element" />, or an
    ///     empty list when the element has no outgoing flows. The result is backed by the
    ///     <see cref="ProcessDefinition.OutgoingBySource" /> index, which rebuilds per access.
    /// </summary>
    public static IReadOnlyList<SequenceFlow> Outgoing(this ProcessDefinition definition, FlowElement element) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(element);
        return definition.OutgoingBySource.TryGetValue(element, out var list) ? list : [];
    }

    /// <summary>
    ///     Returns the sequence flows that target <paramref name="element" />, or an empty list
    ///     when no flow targets it. The result is backed by the
    ///     <see cref="ProcessDefinition.IncomingByTarget" /> index, which rebuilds per access.
    /// </summary>
    public static IReadOnlyList<SequenceFlow> Incoming(this ProcessDefinition definition, FlowElement element) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(element);
        return definition.IncomingByTarget.TryGetValue(element, out var list) ? list : [];
    }

    /// <summary>
    ///     Looks up a flow element by its <see cref="FlowElement.Name" />. The result is backed
    ///     by the <see cref="ProcessDefinition.ByName" /> index.
    /// </summary>
    public static FlowElement? FindElementByName(this ProcessDefinition definition, string? name) {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrEmpty(name)) {
            return null;
        }

        return definition.ByName.TryGetValue(name, out var element) ? element : null;
    }

    /// <summary>
    ///     Returns the first sequence flow that originates from <paramref name="element" />, or
    ///     <see langword="null" /> when none exists.
    /// </summary>
    public static SequenceFlow? FirstOutgoing(this ProcessDefinition definition, FlowElement element) {
        return definition.Outgoing(element).FirstOrDefault();
    }

    /// <summary>
    ///     Returns the first sequence flow that targets <paramref name="element" />, or
    ///     <see langword="null" /> when none exists.
    /// </summary>
    public static SequenceFlow? FirstIncoming(this ProcessDefinition definition, FlowElement element) {
        return definition.Incoming(element).FirstOrDefault();
    }

    /// <summary>
    ///     Returns the single process start event and its single outgoing sequence flow.
    /// </summary>
    public static (FlowEvent Start, SequenceFlow Outgoing) RequireStart(this ProcessDefinition definition) {
        ArgumentNullException.ThrowIfNull(definition);

        var start = definition.Elements.OfType<FlowEvent>().FirstOrDefault(e => e.Position == EventPosition.Start);
        if (start is null) {
            throw new FailedPreconditionException(STATE_MACHINE_NO_START_EVENT);
        }

        var outgoing = definition.Flows.Where(sf => sf.Source == start).ToList();
        if (outgoing.Count != 1) {
            throw new FailedPreconditionException(STATE_MACHINE_START_EVENT_OUTGOING);
        }

        return (start, outgoing[0]);
    }

    /// <summary>
    ///     Returns the single start event and its single outgoing sequence flow inside a sub-process scope.
    /// </summary>
    public static (FlowEvent Start, SequenceFlow Outgoing) RequireStart(this SubProcess subProcess) {
        ArgumentNullException.ThrowIfNull(subProcess);

        var start = subProcess.Children.OfType<FlowEvent>().FirstOrDefault(e => e.Position == EventPosition.Start);
        if (start is null) {
            throw new FailedPreconditionException(
                STATE_MACHINE_NO_START_EVENT,
                new Dictionary<string, string?> { ["name"] = subProcess.Name });
        }

        var outgoing = subProcess.ChildFlows.Where(f => f.Source == start).ToList();
        if (outgoing.Count != 1) {
            throw new FailedPreconditionException(
                STATE_MACHINE_START_EVENT_OUTGOING,
                new Dictionary<string, string?> { ["name"] = subProcess.Name });
        }

        return (start, outgoing[0]);
    }

    /// <summary>
    ///     Returns the first start event inside an event sub-process whose definition matches the predicate.
    /// </summary>
    public static FlowEvent? FindMatchingStart(this EventSubProcess eventSubProcess, Func<IEventDefinition?, bool> matches) {
        ArgumentNullException.ThrowIfNull(eventSubProcess);
        ArgumentNullException.ThrowIfNull(matches);

        return eventSubProcess.Children
                              .OfType<FlowEvent>()
                              .FirstOrDefault(e => e.Position == EventPosition.Start && matches(e.Definition));
    }
}
