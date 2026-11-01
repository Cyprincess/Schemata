using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using Schemata.Abstractions.Advisors;

namespace Schemata.Flow.Foundation.Handlers;

internal sealed class DefaultDeliverSignalHandler<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>(
    FlowHandlerSupport<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource> support
)
    : IRequestHandler<DeliverSignalRequest, SignalDeliveryResult>
    where TProcess : SchemataProcess, new()
    where TToken : SchemataProcessToken, new()
    where TTransition : SchemataProcessTransition, new()
    where TCompensation : SchemataProcessCompensation, new()
    where TParticipant : SchemataProcessParticipant, new()
    where TSource : SchemataProcessSource, new()
{
    public async Task<SignalDeliveryResult> HandleAsync(DeliverSignalRequest request, AdviceContext advice, CancellationToken ct = default) {
        var              delivered = false;
        var              committed = new List<ProcessSnapshot>();
        SchemataProcess? target    = null;

        try {
            await support.Persistence.ExecuteAsync(support.Services, async (scope, current) => {
                committed.Clear();
                delivered = false;

                var process = await scope.Processes.FirstOrDefaultAsync(
                    query => query.Where(candidate => candidate.CanonicalName == request.ProcessCanonicalName),
                    current,
                    new QueryOptions(IgnoreOwner: true));
                if (process is null) {
                    return;
                }

                target = process;
                var registration = support.ResolveRegistration(process);
                var signal = registration.Definition.Signals
                                         .FirstOrDefault(currentSignal => currentSignal.Name == request.SignalName);
                if (signal is null) {
                    return;
                }

                var engine  = support.ResolveEngine(registration);
                var payload = FlowPayload.Deserialize(
                    request.Payload,
                    registration.SignalPayloadTypes.GetValueOrDefault(request.SignalName));
                var tokens  = await FlowHandlerSupport<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>.LoadTokensAsync(scope, process.Name!, current);
                var context = await support.CreateExecutionContextAsync(scope, process, request.Principal, advice, current);
                var targets = await engine.FindTriggerTargetsAsync(
                    registration.Definition, process, tokens, context, signal, current);
                foreach (var token in FlowHandlerSupport<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>.FilterTargets(targets, request.Token)) {
                    if (support.Access is { } access && request.Principal is not null) {
                        await access.RequireEligibilityAsync(FlowOperations.Signal, process,
                            tokens.Where(candidate => candidate.CanonicalName == token).ToArray(), request.Principal, scope.UnitOfWork, current);
                    }
                    var before   = FlowHandlerSupport<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>.WaitingMap(tokens);
                    var snapshot = await engine.TriggerAsync(
                        registration.Definition, process, tokens, context, signal, payload, token, current);
                    support.EnsureCatchesHaveHandlers(registration.Definition, snapshot);
                    await support.RunAdvisorsAsync(registration, scope, context, snapshot, before, advice, current);
                    await support.Persistence.PersistSnapshotAsync(scope, snapshot, current);
                    if (support.Access is { } history) await history.RecordParticipationAsync(process, request.Principal, scope.UnitOfWork, current);
                    committed.Add(snapshot);
                    // The next boundary delivery must see the tokens this delivery routed, so adopt
                    // the committed snapshot's inventory instead of the originally loaded rows.
                    tokens    = snapshot.Tokens.OfType<TToken>().ToList();
                    delivered = true;
                }
            }, ct);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            return new(request.ProcessCanonicalName, SignalDeliveryStatus.Canceled);
        } catch (Exception ex) {
            if (target is not null) {
                await support.Notifier.NotifyFailedAsync(target, ex, CancellationToken.None);
            }

            return new(request.ProcessCanonicalName, SignalDeliveryStatus.Failed, ex);
        }

        if (!delivered) {
            return new(request.ProcessCanonicalName, SignalDeliveryStatus.NoLongerWaiting);
        }

        foreach (var snapshot in committed) {
            await support.NotifyTransitionResultAsync(snapshot, ct);
        }

        return new(request.ProcessCanonicalName, SignalDeliveryStatus.Delivered);
    }
}
