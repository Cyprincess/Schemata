using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using CompleteProcessRequest = Schemata.Flow.Foundation.Commands.CompleteActivityRequest;

namespace Schemata.Flow.Foundation.Handlers;

internal sealed class DefaultCompleteActivityHandler(FlowHandlerSupport support)
    : IRequestHandler<CompleteProcessRequest, ProcessSnapshot>
{
    public async Task<ProcessSnapshot> HandleAsync(CompleteProcessRequest request, CancellationToken ct = default) {
        support.Access?.RequirePermission(FlowOperations.Complete, typeof(SchemataProcess), request.ProcessCanonicalName, request.Principal);
        var process      = await support.LoadProcessAsync(request.ProcessCanonicalName, ct);
        var registration = support.ResolveRegistration(process);
        var engine       = support.ResolveEngine(registration);
        ProcessSnapshot? snapshot = null;

        await support.ExecuteWithNotificationAsync(process, async (scope, current) => {
            var tokens  = await FlowHandlerSupport.LoadTokensAsync(scope, process.Name!, current);
            if (support.Access is { } access) {
                var targets = tokens.Where(token => TokenStates.Live.Contains(token.State!)
                    && (request.Token == null || token.CanonicalName == request.Token)).ToArray();
                await access.RequireEligibilityAsync(FlowOperations.Complete, process, targets, request.Principal, scope, current);
            }
            var before  = FlowHandlerSupport.WaitingMap(tokens);
            var context = await support.CreateExecutionContextAsync(scope, process, request.Principal, current);
            snapshot = await engine.AdvanceAsync(
                registration.Definition, process, tokens, context, request.Token, current);
            support.EnsureCatchesHaveHandlers(registration.Definition, snapshot);
            await support.RunAdvisorsAsync(registration, scope, context, snapshot, before, current);
            await support.Persistence.PersistSnapshotAsync(scope, snapshot, current);
            if (support.Access is { } history) await history.RecordParticipationAsync(process, request.Principal, scope, current);
        }, ct);

        await support.NotifyTransitionResultAsync(snapshot!, ct);
        return snapshot!;
    }
}
