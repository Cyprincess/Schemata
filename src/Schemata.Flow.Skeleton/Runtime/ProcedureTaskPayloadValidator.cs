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

            var catchTypes = IncomingCatchPayloadTypes(definition, task).ToList();
            if (catchTypes.Count == 0 || catchTypes.Any(t => t != payloadType)) {
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

    private static IEnumerable<Type> IncomingCatchPayloadTypes(ProcessDefinition definition, FlowElement target) {
        foreach (var flow in definition.AllFlows.Where(f => f.Target == target)) {
            if (flow.Source is FlowEvent { Position: EventPosition.IntermediateCatch, Definition: Message { PayloadType: not null } message }) {
                yield return message.PayloadType;
            } else if (flow.Source is FlowEvent { Position: EventPosition.IntermediateCatch, Definition: Signal { PayloadType: not null } signal }) {
                yield return signal.PayloadType;
            } else {
                foreach (var type in IncomingCatchPayloadTypes(definition, flow.Source)) {
                    yield return type;
                }
            }
        }
    }
}

/// <summary>
///     Describes a typed procedure task whose incoming catch payload type does not match its payload type.
/// </summary>
/// <param name="Task">The typed procedure task.</param>
/// <param name="ExpectedPayloadType">The payload type required by the task.</param>
/// <param name="IncomingPayloadTypes">The payload types found on upstream message or signal catches.</param>
public sealed record ProcedureTaskPayloadError(
    ProcedureTaskBase Task,
    Type              ExpectedPayloadType,
    IReadOnlyList<Type> IncomingPayloadTypes
);
