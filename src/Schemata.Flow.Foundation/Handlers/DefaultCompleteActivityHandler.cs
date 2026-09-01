using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using CompleteProcessRequest = Schemata.Flow.Foundation.Commands.CompleteActivityRequest;

namespace Schemata.Flow.Foundation.Handlers;

internal sealed class DefaultCompleteActivityHandler(FlowHandlerSupport support)
    : IRequestHandler<CompleteProcessRequest, ProcessSnapshot>
{
    public async Task<ProcessSnapshot> HandleAsync(CompleteProcessRequest request, CancellationToken ct = default) {
        var process      = await support.LoadProcessAsync(request.ProcessCanonicalName, ct);
        var registration = support.ResolveRegistration(process.DefinitionName);
        var engine       = support.ResolveEngine(registration);
        ProcessSnapshot? snapshot = null;

        await support.ExecuteWithNotificationAsync(process, async (scope, current) => {
            var tokens  = await FlowHandlerSupport.LoadTokensAsync(scope, process.Name!, current);
            var before  = FlowHandlerSupport.WaitingMap(tokens);
            var context = await support.CreateExecutionContextAsync(scope, process, request.Principal, current);
            snapshot = await engine.AdvanceAsync(
                registration.Definition, process, tokens, context, request.Token, current);
            support.EnsureCatchesHaveHandlers(registration.Definition, snapshot);
            await support.RunAdvisorsAsync(registration, scope, context, snapshot, before, current);
            await support.Persistence.PersistSnapshotAsync(scope, snapshot, current);
        }, ct);

        await support.NotifyTransitionResultAsync(snapshot!, ct);
        return snapshot!;
    }
}
