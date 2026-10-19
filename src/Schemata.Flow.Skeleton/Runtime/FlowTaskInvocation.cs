using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Engine-neutral dispatch point for <see cref="ProcedureTaskBase" /> bodies. Both built-in engines
///     invoke task bodies through this helper, which derives the stable effect request id, records the
///     intent and its outcome through an installed <see cref="IFlowEffectRecorder" />, and then runs the
///     body with a <see cref="FlowTaskContext" /> carrying <see cref="FlowTaskContext.RequestId" />.
/// </summary>
public static class FlowTaskInvocation
{
    /// <summary>
    ///     Records the effect intent, invokes <paramref name="task" />'s body, and records the outcome.
    ///     When the recorder already holds a <see cref="FlowEffectState.Completed" /> record for the derived
    ///     request id the body is skipped: a completed intent is never re-executed. When it holds an
    ///     <see cref="FlowEffectState.OutcomeUnknown" /> record the unknown outcome is surfaced again rather
    ///     than silently re-executed.
    /// </summary>
    /// <param name="task">The task whose body runs.</param>
    /// <param name="definition">The process definition being executed.</param>
    /// <param name="process">The persisted process instance being advanced.</param>
    /// <param name="token">The token addressed by the task.</param>
    /// <param name="execution">The shared execution context for the current engine call.</param>
    /// <param name="payload">The event payload delivered to typed procedure tasks.</param>
    /// <param name="ct">A cancellation token.</param>
    public static async ValueTask InvokeAsync(
        ProcedureTaskBase    task,
        ProcessDefinition    definition,
        SchemataProcess      process,
        SchemataProcessToken token,
        FlowExecutionContext execution,
        object?              payload,
        CancellationToken    ct
    ) {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(execution);

        if (string.IsNullOrEmpty(token.CanonicalName)) {
            throw new InvalidOperationException("The token has no canonical name; stage it through the joined repository before invoking a task body.");
        }

        if (string.IsNullOrEmpty(process.CanonicalName)) {
            throw new InvalidOperationException("The process has no canonical name; persist it through the joined repository before invoking a task body.");
        }

        // The ordinal pairs the recorder's committed count with the token's in-transition sequence, so a
        // re-driven transition reproduces the rolled-back ids while loop iterations and graph re-entries
        // derive fresh ones.
        var recorder  = execution.Services.GetService<IFlowEffectRecorder>();
        var committed = recorder is not null ? await recorder.CountIntentsAsync(token.CanonicalName, ct) : 0;
        var sequence  = token.Bookkeeping.TryGetValue(FlowConstants.Effects.SequenceKey, out var value) ? value : 0;
        token.Bookkeeping[FlowConstants.Effects.SequenceKey] = sequence + 1;

        var requestId = $"{token.CanonicalName}/{FlowConstants.Effects.RequestIdSegment}/{committed + sequence}";

        if (recorder is not null) {
            var record = await recorder.RecordIntentAsync(new() {
                RequestId = requestId,
                Process   = process.CanonicalName,
                Token     = token.CanonicalName,
                Task      = task.Name,
            }, execution.UnitOfWork, ct);

            if (record.State == FlowEffectState.Completed) {
                return;
            }

            if (record.State == FlowEffectState.OutcomeUnknown) {
                throw new FlowEffectOutcomeUnknownException(
                    new Dictionary<string, string?> { ["name"] = process.CanonicalName, ["task"] = task.Name, ["request"] = requestId });
            }
        }

        var context = new FlowTaskContext(definition, process, token, execution, payload) {
            RequestId      = requestId,
            EffectTaskName = task.Name,
        };

        try {
            await task.InvokeAsync(context, ct);
        } catch (FlowEffectOutcomeUnknownException) {
            if (recorder is not null) {
                await recorder.RecordOutcomeUnknownAsync(requestId, execution.UnitOfWork, ct);
            }

            throw;
        }

        if (recorder is not null) {
            await recorder.RecordCompletionAsync(requestId, execution.UnitOfWork, ct);
        }
    }
}
