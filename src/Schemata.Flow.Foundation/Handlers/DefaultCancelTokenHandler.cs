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

namespace Schemata.Flow.Foundation.Handlers;

internal sealed class DefaultCancelTokenHandler(FlowHandlerSupport support)
    : IRequestHandler<CancelTokenRequest, ProcessSnapshot>
{
    public async Task<ProcessSnapshot> HandleAsync(CancelTokenRequest request, CancellationToken ct = default) {
        support.Access?.RequirePermission(FlowOperations.Cancel, typeof(SchemataProcessToken), request.TokenCanonicalName, request.Principal);
        var process      = await support.LoadProcessAsync(request.ProcessCanonicalName, ct);
        var registration = support.ResolveRegistration(process);
        ProcessSnapshot? snapshot = null;

        await support.ExecuteWithNotificationAsync(process, async (scope, current) => {
            var tokens  = await FlowHandlerSupport.LoadTokensAsync(scope, process.Name!, current);
            var context = await support.CreateExecutionContextAsync(scope, process, request.Principal, current);
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
            if (support.Access is { } access) await access.RequireEligibilityAsync(FlowOperations.Cancel, process, [target], request.Principal, scope, current);

            var before   = FlowHandlerSupport.WaitingMap(tokens);
            var previous = target.WaitingAtName ?? target.StateName;
            target.State         = "Cancelled";
            target.WaitingAtName = null;

            var transition = FlowHandlerSupport.CancelTransition(
                process, target, previous, "Cancelled", "CancelToken", request.Principal);
            if (tokens.All(token => TokenStates.IsTerminal(token.State))) {
                process.State = "Cancelled";
            }

            snapshot = new() { Process = process, Tokens = tokens, Transitions = [transition] };
            await support.RunAdvisorsAsync(registration, scope, context, snapshot, before, current);
            await support.Persistence.PersistSnapshotAsync(scope, snapshot, current);
            if (support.Access is { } history) await history.RecordParticipationAsync(process, request.Principal, scope, current);
        }, ct);

        await support.NotifyTransitionResultAsync(snapshot!, ct);
        return snapshot!;
    }
}
