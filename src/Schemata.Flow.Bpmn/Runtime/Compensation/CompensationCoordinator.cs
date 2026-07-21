using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Schemata.Flow.Bpmn.Runtime.Compensation;

/// <summary>Runs scope compensation handlers in BPMN reverse completion order.</summary>
public static class CompensationCoordinator
{
    /// <summary>Invokes handlers from the stack snapshot in reverse order, stopping at the first failure.</summary>
    /// <param name="stack">The scope compensation stack.</param>
    /// <param name="context">The compensation invocation payload.</param>
    /// <param name="observers">Lifecycle observers to notify around compensation.</param>
    /// <param name="ct">Cancellation token for observer and handler calls.</param>
    /// <param name="logger">Optional logger for non-fatal observer failures.</param>
    /// <returns>Compensated handlers and any failure for the caller to map to BPMN errors.</returns>
    public static async ValueTask<CompensationResult> InvokeAllAsync(
        CompensationStack                              stack,
        CompensationInvocationContext                  context,
        IEnumerable<ICompensationLifecycleObserver>    observers,
        CancellationToken                              ct = default,
        ILogger?                                      logger = null) {
        var snapshot    = stack.Snapshot();
        var compensated = new List<ICompensationHandler>(snapshot.Count);

        for (var i = snapshot.Count - 1; i >= 0; i--) {
            var handler = snapshot[i];

            await NotifyStartedAsync(observers, context, logger, ct);

            try {
                await handler.InvokeAsync(context, ct);
            }
            catch (Exception ex) {
                return new(compensated, handler, ex);
            }

            compensated.Add(handler);
        }

        await NotifyCompletedAsync(observers, context, logger, ct);

        return new(compensated, null, null);
    }

    private static async Task NotifyStartedAsync(
        IEnumerable<ICompensationLifecycleObserver> observers,
        CompensationInvocationContext               context,
        ILogger?                                    logger,
        CancellationToken                           ct) {
        foreach (var observer in observers) {
            try {
                await observer.OnCompensationStartedAsync(context.Process, context.Scope, ct);
            }
            catch (Exception ex) {
                logger?.LogWarning(ex, "Compensation lifecycle observer failed while notifying compensation start.");
            }
        }
    }

    private static async Task NotifyCompletedAsync(
        IEnumerable<ICompensationLifecycleObserver> observers,
        CompensationInvocationContext               context,
        ILogger?                                    logger,
        CancellationToken                           ct) {
        foreach (var observer in observers) {
            try {
                await observer.OnCompensationCompletedAsync(context.Process, context.Scope, ct);
            }
            catch (Exception ex) {
                logger?.LogWarning(ex, "Compensation lifecycle observer failed while notifying compensation completion.");
            }
        }
    }
}
