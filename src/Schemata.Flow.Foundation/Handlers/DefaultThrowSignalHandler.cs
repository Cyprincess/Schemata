using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Activity = Schemata.Flow.Skeleton.Models.Activity;
using EventPosition = Schemata.Flow.Skeleton.Models.EventPosition;
using FlowEvent = Schemata.Flow.Skeleton.Models.FlowEvent;
using Signal = Schemata.Flow.Skeleton.Models.Signal;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Commands;

namespace Schemata.Flow.Foundation.Handlers;

internal sealed class DefaultThrowSignalHandler(FlowHandlerSupport support)
    : IRequestHandler<ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>
{
    public async Task<IReadOnlyList<SignalDeliveryResult>> HandleAsync(
        ThrowSignalRequest request,
        CancellationToken ct = default
    ) {
        support.Access?.RequirePermission(FlowOperations.Signal, typeof(SchemataProcess), null, request.Principal);
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
        var candidates = new HashSet<string>(StringComparer.Ordinal);

        await using (var scope = support.Scopes.CreateAsyncScope()) {
            await foreach (var process in support.Persistence.ListWaitingAsync(scope.ServiceProvider, ct)) {
                if (IsSignalCandidate(process, signalName)) {
                    candidates.Add(process.CanonicalName!);
                }
            }

            // Boundary signal catches armed on an active host carry no waiting token, so the
            // waiting-only listing cannot see them; enumerate live hosts and match the signal
            // against the definition's boundary catches.
            await foreach (var process in support.Persistence.ListActiveHostsAsync(scope.ServiceProvider, ct)) {
                if (string.IsNullOrEmpty(process.CanonicalName) || candidates.Contains(process.CanonicalName)) {
                    continue;
                }

                ProcessRegistration registration;
                try {
                    registration = support.ResolveRegistration(process);
                } catch (FailedPreconditionException) {
                    candidates.Add(process.CanonicalName);
                    continue;
                }

                var hasBoundaryCatch = registration.Definition.AllElements.OfType<FlowEvent>().Any(
                    evt => evt is { Position: EventPosition.Boundary, AttachedTo: Activity, Definition: Signal signal }
                        && signal.Name == signalName);
                if (hasBoundaryCatch) {
                    candidates.Add(process.CanonicalName);
                }
            }
        }

        return candidates.Order(StringComparer.Ordinal).ToList();
    }

    private bool IsSignalCandidate(SchemataProcess process, string signalName) {
        if (string.IsNullOrEmpty(process.CanonicalName)) {
            return false;
        }

        ProcessRegistration registration;
        try {
            registration = support.ResolveRegistration(process);
        } catch (FailedPreconditionException) {
            return true;
        }

        return registration.Definition.Signals.Any(signal => signal.Name == signalName);
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
            var inner = new DeliverSignalRequest(
                processCanonicalName,
                request.SignalName,
                request.Payload,
                request.Token,
                request.Principal);
            var result = await dispatcher.SendAsync<ResourceMethodRequest<SchemataProcess, DeliverSignalRequest, SignalDeliveryResult>, SignalDeliveryResult>(
                new(FlowOperations.Deliver, processCanonicalName, inner, request.Principal), ct);
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
