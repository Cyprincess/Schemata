using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Messaging.Skeleton;
using Schemata.Abstractions.Advisors;

namespace Schemata.Flow.Foundation.Handlers;

internal sealed class DefaultTerminateProcessHandler<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>(
    FlowHandlerSupport<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource> support
)
    : IRequestHandler<TerminateProcessRequest, ProcessSnapshot>
    where TProcess : SchemataProcess, new()
    where TToken : SchemataProcessToken, new()
    where TTransition : SchemataProcessTransition, new()
    where TCompensation : SchemataProcessCompensation, new()
    where TParticipant : SchemataProcessParticipant, new()
    where TSource : SchemataProcessSource, new()
{
    public async Task<ProcessSnapshot> HandleAsync(TerminateProcessRequest request, AdviceContext advice, CancellationToken ct = default) {
        var process      = await support.LoadProcessAsync(request.ProcessCanonicalName, ct);
        var registration = support.ResolveRegistration(process);
        ProcessSnapshot? snapshot = null;

        await support.ExecuteWithNotificationAsync(process, async (scope, current) => {
            var tokens = await FlowHandlerSupport<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>.LoadTokensAsync(scope, process.Name!, current);
            if (support.Access is { } access && request.Principal is not null) {
                await access.RequireEligibilityAsync(FlowOperations.Terminate, process, [], request.Principal, scope.UnitOfWork, current);
            }
            var before      = FlowHandlerSupport<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>.WaitingMap(tokens);
            var context     = await support.CreateExecutionContextAsync(scope, process, request.Principal, advice, current);
            var transitions = new List<TTransition>();
            foreach (var token in tokens) {
                if (TokenStates.IsTerminal(token.State)) {
                    continue;
                }

                var previous = token.WaitingAtName ?? token.StateName;
                token.State                 = "Cancelled";
                token.WaitingAtName         = null;
                token.CurrentCallActivation = null;
                transitions.Add(FlowHandlerSupport<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>.CancelTransition(
                    process, token, previous, "Terminated", "Terminate", request.Principal));
            }

            process.State = "Terminated";
            snapshot = new() { Process = process, Tokens = tokens, Transitions = transitions };
            await support.RunAdvisorsAsync(registration, scope, context, snapshot, before, advice, current);
            await support.Persistence.PersistSnapshotAsync(scope, snapshot, current);
            if (support.Access is { } history) await history.RecordParticipationAsync(process, request.Principal, scope.UnitOfWork, current);
        }, ct);

        await support.Notifier.NotifyTransitionedAsync(snapshot!, ct);
        await support.Notifier.NotifyTerminatedAsync(process, ct);
        return snapshot!;
    }
}
