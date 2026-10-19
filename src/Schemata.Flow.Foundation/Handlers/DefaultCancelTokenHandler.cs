using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Messaging.Skeleton;
using Schemata.Abstractions.Advisors;

namespace Schemata.Flow.Foundation.Handlers;

internal sealed class DefaultCancelTokenHandler<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>(
    FlowHandlerSupport<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource> support
)
    : IRequestHandler<CancelTokenRequest, ProcessSnapshot>
    where TProcess : SchemataProcess, new()
    where TToken : SchemataProcessToken, new()
    where TTransition : SchemataProcessTransition, new()
    where TCompensation : SchemataProcessCompensation, new()
    where TParticipant : SchemataProcessParticipant, new()
    where TSource : SchemataProcessSource, new()
{
    public async Task<ProcessSnapshot> HandleAsync(CancelTokenRequest request, AdviceContext advice, CancellationToken ct = default) {
        var process      = await support.LoadProcessAsync(request.ProcessCanonicalName, ct);
        var registration = support.ResolveRegistration(process);
        ProcessSnapshot? snapshot = null;

        await support.ExecuteWithNotificationAsync(process, async (scope, current) => {
            var tokens  = await FlowHandlerSupport<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>.LoadTokensAsync(scope, process.Name!, current);
            var context = await support.CreateExecutionContextAsync(scope, process, request.Principal, advice, current);
            var target  = tokens.FirstOrDefault(token => token.CanonicalName == request.TokenCanonicalName);
            if (target is null) {
                throw new NotFoundException(
                    SchemataResources.PROCESS_TOKEN_NOT_FOUND,
                    new Dictionary<string, string?> {
                        ["token"] = request.TokenCanonicalName,
                        ["process"] = process.CanonicalName,
                    }
                );
            }

            if (TokenStates.IsTerminal(target.State)) {
                throw new FailedPreconditionException(
                    SchemataResources.PROCESS_TOKEN_NOT_READY,
                    new Dictionary<string, string?> {
                        ["token"] = request.TokenCanonicalName,
                    });
            }
            if (support.Access is { } access && request.Principal is not null) {
                await access.RequireEligibilityAsync(FlowOperations.Cancel, process, [target], request.Principal, scope.UnitOfWork, current);
            }

            var before   = FlowHandlerSupport<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>.WaitingMap(tokens);
            var previous = target.WaitingAtName ?? target.StateName;
            target.State                 = "Cancelled";
            target.WaitingAtName         = null;
            target.CurrentCallActivation = null;

            var transition = FlowHandlerSupport<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>.CancelTransition(
                process, target, previous, "Cancelled", "CancelToken", request.Principal);
            if (tokens.All(token => TokenStates.IsTerminal(token.State))) {
                process.State = "Cancelled";
            }

            snapshot = new() { Process = process, Tokens = tokens, Transitions = [transition] };
            await support.RunAdvisorsAsync(registration, scope, context, snapshot, before, advice, current);
            await support.Persistence.PersistSnapshotAsync(scope, snapshot, current);
            if (support.Access is { } history) await history.RecordParticipationAsync(process, request.Principal, scope.UnitOfWork, current);
        }, ct);

        await support.NotifyTransitionResultAsync(snapshot!, ct);
        return snapshot!;
    }
}
