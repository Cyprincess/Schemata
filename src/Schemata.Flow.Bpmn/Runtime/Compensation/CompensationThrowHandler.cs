using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Bpmn.Runtime.Compensation;

/// <summary>Dispatches BPMN compensation throw events against the current scope stack.</summary>
public sealed class CompensationThrowHandler
{
    /// <summary>Fires a BPMN compensation throw in targeted or global mode.</summary>
    /// <param name="engine">The BPMN engine that owns per-scope compensation stacks.</param>
    /// <param name="definition">The active process definition.</param>
    /// <param name="process">The process instance being advanced.</param>
    /// <param name="throwing">The token that reached the compensation throw event.</param>
    /// <param name="working">The mutable token snapshot used by the engine.</param>
    /// <param name="compensation">The compensation throw definition.</param>
    /// <param name="execution">The execution context containing restored compensation bindings.</param>
    /// <param name="observers">Lifecycle observers notified around compensation work.</param>
    /// <param name="ct">Cancellation token for observer and handler calls.</param>
    /// <returns>Compensation transitions written by invoked handlers.</returns>
    public async ValueTask<IReadOnlyList<SchemataProcessTransition>> FireAsync(
        BpmnEngine                             engine,
        ProcessDefinition                      definition,
        SchemataProcess                        process,
        SchemataProcessToken                   throwing,
        IReadOnlyList<SchemataProcessToken>    working,
        CompensationDefinition                 compensation,
        FlowExecutionContext                   execution,
        IEnumerable<ICompensationLifecycleObserver> observers,
        CancellationToken                      ct = default) {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(throwing);
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(compensation);
        ArgumentNullException.ThrowIfNull(observers);

        var result = await FireForEngineAsync(engine, definition, process, throwing, working, compensation, execution, observers, ct);
        if (result.FailureReason is not null) {
            throw result.FailureReason;
        }

        return result.Transitions;
    }

    internal async ValueTask<CompensationThrowResult> FireForEngineAsync(
        BpmnEngine                             engine,
        ProcessDefinition                      definition,
        SchemataProcess                        process,
        SchemataProcessToken                   throwing,
        IReadOnlyList<SchemataProcessToken>    working,
        CompensationDefinition                 compensation,
        FlowExecutionContext                   execution,
        IEnumerable<ICompensationLifecycleObserver> observers,
        CancellationToken                      ct = default) {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(throwing);
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(compensation);
        ArgumentNullException.ThrowIfNull(observers);

        var stack = engine.TryGetCompensationStack(definition, process, throwing, working, execution);
        if (stack is null || stack.Count == 0) {
            throw new InvalidOperationException($"Compensation binding is missing for scope '{throwing.ScopeName}'.");
        }

        var context = NewContext(process, definition, throwing);
        return compensation.Activity is { } target
            ? await FireTargetedAsync(stack, context, target, execution, observers, ct)
            : await FireGlobalAsync(stack, context, execution, observers, ct);
    }

    private static async ValueTask<CompensationThrowResult> FireTargetedAsync(
        CompensationStack                           stack,
        CompensationInvocationContext               context,
        Activity                                    target,
        FlowExecutionContext                        execution,
        IEnumerable<ICompensationLifecycleObserver> observers,
        CancellationToken                           ct) {
        var snapshot = stack.Snapshot();
        for (var i = snapshot.Count - 1; i >= 0; i--) {
            var handler = snapshot[i];
            if (!ReferenceEquals(handler.Activity, target)) {
                continue;
            }

            await NotifyStartedAsync(observers, context, execution, ct);
            try {
                await handler.InvokeAsync(context, ct);
            }
            catch (Exception ex) {
                return CompensationThrowResult.FromFailure([.. context.Transitions], handler, ex);
            }

            await NotifyCompletedAsync(observers, context, execution, ct);
            stack.Remove(handler);
            return CompensationThrowResult.Completed([.. context.Transitions]);
        }

        throw new InvalidOperationException($"Compensation binding is missing for activity '{target.Name}'.");
    }

    private static async ValueTask<CompensationThrowResult> FireGlobalAsync(
        CompensationStack                           stack,
        CompensationInvocationContext               context,
        FlowExecutionContext                        execution,
        IEnumerable<ICompensationLifecycleObserver> observers,
        CancellationToken                           ct) {
        var result = await CompensationCoordinator.InvokeAllAsync(
            stack,
            context,
            observers,
            ct,
            execution.Services.GetService<ILoggerFactory>()?.CreateLogger(typeof(CompensationCoordinator).FullName!));
        foreach (var handler in result.Compensated) {
            stack.Remove(handler);
        }

        if (result.Failed is null) {
            stack.Clear();
            return CompensationThrowResult.Completed([.. context.Transitions]);
        }

        return CompensationThrowResult.FromFailure(
            [.. context.Transitions],
            result.Failed,
            result.FailureReason ?? new InvalidOperationException("BPMN compensation failed."));
    }

    private static CompensationInvocationContext NewContext(
        SchemataProcess      process,
        ProcessDefinition    definition,
        SchemataProcessToken throwing) {
        return new(
            process,
            definition,
            BpmnEngine.TokenView(throwing),
            new Dictionary<string, int>(throwing.Bookkeeping, StringComparer.Ordinal));
    }

    private static async Task NotifyStartedAsync(
        IEnumerable<ICompensationLifecycleObserver> observers,
        CompensationInvocationContext               context,
        FlowExecutionContext                        execution,
        CancellationToken                           ct) {
        var logger = execution.Services.GetService<ILogger<CompensationThrowHandler>>();
        foreach (var observer in observers) {
            try {
                await observer.OnCompensationStartedAsync(context.Process, context.Scope, ct);
            } catch (Exception ex) {
                logger?.LogWarning(ex, "Compensation lifecycle observer failed while notifying compensation start.");
            }
        }
    }

    private static async Task NotifyCompletedAsync(
        IEnumerable<ICompensationLifecycleObserver> observers,
        CompensationInvocationContext               context,
        FlowExecutionContext                        execution,
        CancellationToken                           ct) {
        var logger = execution.Services.GetService<ILogger<CompensationThrowHandler>>();
        foreach (var observer in observers) {
            try {
                await observer.OnCompensationCompletedAsync(context.Process, context.Scope, ct);
            } catch (Exception ex) {
                logger?.LogWarning(ex, "Compensation lifecycle observer failed while notifying compensation completion.");
            }
        }
    }

    internal sealed record CompensationThrowResult(
        IReadOnlyList<SchemataProcessTransition> Transitions,
        ICompensationHandler?                    Failed,
        Exception?                               FailureReason) {
        internal static CompensationThrowResult Completed(IReadOnlyList<SchemataProcessTransition> transitions) => new(transitions, null, null);

        internal static CompensationThrowResult FromFailure(
            IReadOnlyList<SchemataProcessTransition> transitions,
            ICompensationHandler                    handler,
            Exception                               failure) => new(transitions, handler, failure);
    }
}
