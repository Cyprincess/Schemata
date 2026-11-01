using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Expressions.Skeleton;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

internal static class FlowTaskBinding
{
    internal static IReadOnlyDictionary<string, string> FreezeOutputs(IReadOnlyDictionary<string, string> outputs) {
        return outputs.Count == 0 ? FrozenDictionary<string, string>.Empty : outputs.ToFrozenDictionary(StringComparer.Ordinal);
    }

    internal static void RequireBindings<TInput, TResult>(
        ProcedureTaskBase task,
        Func<FlowTaskContext, CancellationToken, ValueTask<TInput>>? input,
        Func<FlowTaskContext, TResult, CancellationToken, ValueTask>? output
    ) {
        if (input is null || output is null) {
            throw Error(task, SchemataResources.FLOW_TASK_BINDING_REQUIRED, "binding", input is null ? "Input" : "Output");
        }
    }

    internal static async ValueTask<TInput> ReadInputAsync<TInput>(
        ProcedureTaskBase task,
        Func<FlowTaskContext, CancellationToken, ValueTask<TInput>> input,
        FlowTaskContext context,
        CancellationToken ct
    ) {
        try {
            return await input(context, ct);
        } catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException) {
            throw Error(task, SchemataResources.FLOW_TASK_INPUT_INVALID, "input", typeof(TInput).FullName, innerException: exception);
        }
    }

    internal static async ValueTask ApplyOutputAsync<TResult>(
        ProcedureTaskBase task,
        IReadOnlyDictionary<string, string> mapping,
        TResult result,
        Func<FlowTaskContext, TResult, CancellationToken, ValueTask> output,
        FlowTaskContext context,
        CancellationToken ct
    ) {
        ct.ThrowIfCancellationRequested();
        if (result is null) {
            throw Error(task, SchemataResources.FLOW_TASK_OUTPUT_INVALID, "output", typeof(TResult).FullName);
        }

        if (mapping.Count != 0) {
            if (result is not IReadOnlyDictionary<string, object?> values) {
                throw Error(task, SchemataResources.FLOW_TASK_OUTPUT_INVALID, "output", typeof(TResult).FullName);
            }

            var annotations = new KeyValuePair<string, string?>[mapping.Count];
            var index = 0;
            foreach (var (field, annotation) in mapping) {
                if (string.IsNullOrEmpty(annotation) || !values.TryGetValue(field, out var value)) {
                    throw Error(task, SchemataResources.FLOW_TASK_OUTPUT_INVALID, "output", field);
                }

                try {
                    annotations[index++] = new(annotation, value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture));
                } catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException) {
                    throw Error(task, SchemataResources.FLOW_TASK_OUTPUT_INVALID, "output", field, innerException: exception);
                }
            }

            foreach (var (annotation, value) in annotations) {
                context.Token.Annotations[annotation] = value;
            }
        }

        try {
            await output(context, result, ct);
            ct.ThrowIfCancellationRequested();
        } catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException) {
            throw Error(task, SchemataResources.FLOW_TASK_OUTPUT_INVALID, "output", typeof(TResult).FullName, innerException: exception);
        }
    }

    internal static FailedPreconditionException Error(
        ProcedureTaskBase task,
        string reason,
        string kind,
        string? binding,
        string? language = null,
        Exception? innerException = null,
        IExpressionError? expressionError = null
    ) {
        var metadata = new Dictionary<string, string?> {
            ["name"] = task.Name,
            ["kind"] = kind,
            ["binding"] = binding,
            ["language"] = language,
            ["decision"] = binding,
            ["version"] = language,
        };
        if (expressionError is not null) {
            metadata["expression_reason"] = expressionError.Reason;
            metadata["expression_message"] = expressionError.Message;
        }
        return new(
            [new PreconditionViolation { Type = kind, Subject = task.Name, Description = binding }],
            reason,
            metadata,
            innerException);
    }
}
