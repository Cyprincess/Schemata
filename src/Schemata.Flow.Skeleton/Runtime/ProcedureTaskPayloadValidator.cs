using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Validates that typed procedure tasks are reachable only through matching typed message or signal catches.
/// </summary>
public static class ProcedureTaskPayloadValidator
{
    /// <summary>Returns mismatched typed procedure task paths in the supplied process definition.</summary>
    /// <param name="definition">The process definition to inspect.</param>
    public static IReadOnlyList<ProcedureTaskPayloadError> FindErrors(ProcessDefinition definition) {
        ArgumentNullException.ThrowIfNull(definition);

        var result = new List<ProcedureTaskPayloadError>();
        foreach (var task in definition.AllElements.OfType<ProcedureTaskBase>()) {
            var payloadType = GetPayloadType(task);
            if (payloadType is null) {
                continue;
            }

            var (catchTypes, payloadFreePath) = IncomingPayloadTrace(definition, task, []);
            if (payloadFreePath || catchTypes.Count == 0 || catchTypes.Any(t => t != payloadType)) {
                result.Add(new(task, payloadType, catchTypes));
            }
        }

        return result;
    }

    /// <summary>Throws when any typed procedure task has no matching typed message or signal catch.</summary>
    /// <param name="definition">The process definition to inspect.</param>
    public static void Validate(ProcessDefinition definition) {
        var errors = FindErrors(definition);
        if (errors.Count > 0) {
            var names = string.Join(", ", errors.Select(e => e.Task.Name));
            throw new InvalidOperationException($"Typed procedure task payload mismatch: {names}.");
        }
    }

    private static Type? GetPayloadType(ProcedureTaskBase task) {
        var type = task.GetType();
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ProcedureTask<>)
            ? type.GetGenericArguments()[0]
            : null;
    }

    private static (IReadOnlyList<Type> Types, bool HasPayloadFreePath) IncomingPayloadTrace(
        ProcessDefinition       definition,
        FlowElement             target,
        HashSet<FlowElement>    visited
    ) {
        var types = new List<Type>();
        var payloadFree = false;
        var walkedIncoming = false;

        // The walk is bounded by a visited set: a legal durable cycle (typed catch → task → waiting
        // user task → gateway → back to the catch) revisits elements, and without the bound the
        // recursion never terminates.
        foreach (var flow in definition.AllFlows.Where(f => f.Target == target)) {
            walkedIncoming = true;
            switch (flow.Source) {
                case FlowEvent { Position: EventPosition.IntermediateCatch, Definition: Message { PayloadType: not null } message }:
                    types.Add(message.PayloadType);
                    break;
                case FlowEvent { Position: EventPosition.IntermediateCatch, Definition: Signal { PayloadType: not null } signal }:
                    types.Add(signal.PayloadType);
                    break;
                case null:
                    payloadFree = true;
                    break;
                default:
                    if (visited.Add(flow.Source)) {
                        var (sourceTypes, sourcePayloadFree) = IncomingPayloadTrace(definition, flow.Source, visited);
                        types.AddRange(sourceTypes);
                        payloadFree |= sourcePayloadFree;
                    }
                    break;
            }
        }

        // An element with no incoming flows (a start event, or a boundary-style root) contributes a
        // path that reaches the task without any typed catch delivering the payload.
        if (!walkedIncoming) {
            payloadFree = true;
        }

        return (types, payloadFree);
    }
}
