using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Flow.Foundation.Handlers;

internal sealed class DefaultTerminateProcessHandler(FlowHandlerSupport support)
    : IRequestHandler<TerminateProcessRequest, ProcessSnapshot>
{
    public async Task<ProcessSnapshot> HandleAsync(TerminateProcessRequest request, CancellationToken ct = default) {
        var process      = await support.LoadProcessAsync(request.ProcessCanonicalName, ct);
        var registration = support.ResolveRegistration(process.DefinitionName);
        ProcessSnapshot? snapshot = null;

        await support.ExecuteWithNotificationAsync(process, async (scope, current) => {
            var tokens      = await FlowHandlerSupport.LoadTokensAsync(scope, process.Name!, current);
            var before      = FlowHandlerSupport.WaitingMap(tokens);
            var context     = await support.CreateExecutionContextAsync(scope, process, request.Principal, current);
            var transitions = new List<SchemataProcessTransition>();
            foreach (var token in tokens) {
                var previous = token.WaitingAtName ?? token.StateName;
                token.State         = "Cancelled";
                token.WaitingAtName = null;
                transitions.Add(FlowHandlerSupport.CancelTransition(
                    process, token, previous, "Terminated", "Terminate", request.Principal));
            }

            process.State = "Terminated";
            snapshot = new() { Process = process, Tokens = tokens, Transitions = transitions };
            await support.RunAdvisorsAsync(registration, scope, context, snapshot, before, current);
            await support.Persistence.PersistSnapshotAsync(scope, snapshot, current);
        }, ct);

        await support.Notifier.NotifyTransitionedAsync(snapshot!, ct);
        await support.Notifier.NotifyTerminatedAsync(process, ct);
        return snapshot!;
    }
}
