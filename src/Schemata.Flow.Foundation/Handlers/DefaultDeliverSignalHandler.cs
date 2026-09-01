using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Flow.Foundation.Handlers;

internal sealed class DefaultDeliverSignalHandler(FlowHandlerSupport support)
    : IRequestHandler<DeliverSignalRequest, SignalDeliveryResult>
{
    public async Task<SignalDeliveryResult> HandleAsync(DeliverSignalRequest request, CancellationToken ct = default) {
        var              delivered = false;
        var              committed = new List<ProcessSnapshot>();
        SchemataProcess? target    = null;

        try {
            await support.Persistence.ExecuteAsync(support.Services, async (scope, current) => {
                committed.Clear();
                delivered = false;

                var process = await scope.Processes.FirstOrDefaultAsync(
                    query => query.Where(candidate => candidate.CanonicalName == request.ProcessCanonicalName), current);
                if (process is null) {
                    return;
                }

                target = process;
                var registration = support.FindRegistration(process.DefinitionName);
                var signal = registration?.Definition.Signals
                                          .FirstOrDefault(currentSignal => currentSignal.Name == request.SignalName);
                if (registration is null || signal is null) {
                    return;
                }

                var engine  = support.ResolveEngine(registration);
                var payload = FlowHandlerSupport.DeserializePayload(
                    request.Payload,
                    registration.SignalPayloadTypes.GetValueOrDefault(request.SignalName));
                var tokens  = await FlowHandlerSupport.LoadTokensAsync(scope, process.Name!, current);
                var context = await support.CreateExecutionContextAsync(scope, process, request.Principal, current);
                var targets = await engine.FindTriggerTargetsAsync(
                    registration.Definition, process, tokens, context, signal, current);
                foreach (var token in FlowHandlerSupport.FilterTargets(targets, request.Token)) {
                    var before   = FlowHandlerSupport.WaitingMap(tokens);
                    var snapshot = await engine.TriggerAsync(
                        registration.Definition, process, tokens, context, signal, payload, token, current);
                    support.EnsureCatchesHaveHandlers(registration.Definition, snapshot);
                    await support.RunAdvisorsAsync(registration, scope, context, snapshot, before, current);
                    await support.Persistence.PersistSnapshotAsync(scope, snapshot, current);
                    committed.Add(snapshot);
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
