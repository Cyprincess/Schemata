using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Flow.Foundation.Commands;
using Schemata.Messaging.Skeleton;

namespace Schemata.Flow.Foundation.Handlers;

internal sealed class DefaultThrowSignalHandler(FlowHandlerSupport support)
    : IRequestHandler<ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>
{
    public async Task<IReadOnlyList<SignalDeliveryResult>> HandleAsync(
        ThrowSignalRequest request,
        CancellationToken ct = default
    ) {
        var candidates = await SnapshotSignalCandidatesAsync(request.SignalName, ct);
        if (candidates.Count == 0) {
            return [];
        }

        var concurrency = support.SignalBroadcastConcurrency;
        var results     = new SignalDeliveryResult?[candidates.Count];
        var pending     = new List<Task<(int Index, SignalDeliveryResult Result)>>(concurrency);

        using var gate = new SemaphoreSlim(concurrency, concurrency);
        try {
            for (var index = 0; index < candidates.Count; index++) {
                if (ct.IsCancellationRequested) {
                    results[index] = new(candidates[index], SignalDeliveryStatus.Canceled);
                    continue;
                }

                try {
                    await gate.WaitAsync(ct);
                } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                    results[index] = new(candidates[index], SignalDeliveryStatus.Canceled);
                    continue;
                }

                pending.Add(DeliverInOwnScopeAsync(index, candidates[index], request, gate, ct));
                if (pending.Count >= concurrency) {
                    await DrainOneAsync(pending, results);
                }
            }
        } finally {
            while (pending.Count > 0) {
                await DrainOneAsync(pending, results);
            }
        }

        return results.Select(result => result!).ToList();
    }

    private async ValueTask<IReadOnlyList<string>> SnapshotSignalCandidatesAsync(
        string signalName,
        CancellationToken ct
    ) {
        var candidates = new List<string>();

        await using (var scope = support.Scopes.CreateAsyncScope()) {
            await foreach (var process in support.Persistence.ListWaitingAsync(scope.ServiceProvider, ct)) {
                if (string.IsNullOrEmpty(process.CanonicalName)) {
                    continue;
                }

                var registration = support.FindRegistration(process.DefinitionName);
                if (registration?.Definition.Signals.Any(signal => signal.Name == signalName) == true) {
                    candidates.Add(process.CanonicalName);
                }
            }
        }

        candidates.Sort(StringComparer.Ordinal);
        return candidates;
    }

    private async Task<(int Index, SignalDeliveryResult Result)> DeliverInOwnScopeAsync(
        int                index,
        string             processCanonicalName,
        ThrowSignalRequest request,
        SemaphoreSlim      gate,
        CancellationToken  ct
    ) {
        try {
            await using var scope = support.Scopes.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
            var result = await dispatcher.SendAsync<DeliverSignalRequest, SignalDeliveryResult>(new(
                processCanonicalName,
                request.SignalName,
                request.Payload,
                request.Token,
                request.Principal), ct);
            return (index, result);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            return (index, new(processCanonicalName, SignalDeliveryStatus.Canceled));
        } catch (Exception ex) {
            return (index, new(processCanonicalName, SignalDeliveryStatus.Failed, ex));
        } finally {
            gate.Release();
        }
    }

    private static async Task DrainOneAsync(
        List<Task<(int Index, SignalDeliveryResult Result)>> pending,
        SignalDeliveryResult?[]                              results
    ) {
        var completed = await Task.WhenAny(pending);
        pending.Remove(completed);
        var (index, result) = await completed;
        results[index] = result;
    }
}
