using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using CompleteProcessRequest = Schemata.Flow.Foundation.Commands.CompleteActivityRequest;
using Schemata.Abstractions.Advisors;

namespace Schemata.Flow.Foundation.Handlers;

internal sealed class DefaultCompleteActivityHandler<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>(
    FlowHandlerSupport<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource> support
)
    : IRequestHandler<CompleteProcessRequest, ProcessSnapshot>
    where TProcess : SchemataProcess, new()
    where TToken : SchemataProcessToken, new()
    where TTransition : SchemataProcessTransition, new()
    where TCompensation : SchemataProcessCompensation, new()
    where TParticipant : SchemataProcessParticipant, new()
    where TSource : SchemataProcessSource, new()
{
    public async Task<ProcessSnapshot> HandleAsync(CompleteProcessRequest request, AdviceContext advice, CancellationToken ct = default) {
        var process      = await support.LoadProcessAsync(request.ProcessCanonicalName, ct);
        var registration = support.ResolveRegistration(process);
        var engine       = support.ResolveEngine(registration);
        ProcessSnapshot? snapshot = null;

        await support.ExecuteWithNotificationAsync(process, async (scope, current) => {
            var tokens  = await FlowHandlerSupport<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>.LoadTokensAsync(scope, process.Name!, current);
            if (support.Access is { } access && request.Principal is not null) {
                var targets = tokens.Where(token => TokenStates.Live.Contains(token.State!)
                    && (request.Token == null || token.CanonicalName == request.Token)).ToArray();
                await access.RequireEligibilityAsync(FlowOperations.Complete, process, targets, request.Principal, scope.UnitOfWork, current);
            }
            var before  = FlowHandlerSupport<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>.WaitingMap(tokens);
            var context = await support.CreateExecutionContextAsync(scope, process, request.Principal, advice, current);
            snapshot = await engine.AdvanceAsync(
                registration.Definition, process, tokens, context, request.Token, current);
            support.EnsureCatchesHaveHandlers(registration.Definition, snapshot);
            await support.RunAdvisorsAsync(registration, scope, context, snapshot, before, advice, current);
            await support.Persistence.PersistSnapshotAsync(scope, snapshot, current);
            if (support.Access is { } history) await history.RecordParticipationAsync(process, request.Principal, scope.UnitOfWork, current);
        }, ct);

        await support.NotifyTransitionResultAsync(snapshot!, ct);
        return snapshot!;
    }
}
