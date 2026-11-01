using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using Schemata.Abstractions.Advisors;

namespace Schemata.Flow.Foundation.Handlers;

internal sealed class DefaultStartProcessHandler<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>(
    FlowHandlerSupport<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource> support
)
    : IRequestHandler<StartProcessRequest, SchemataProcess>
    where TProcess : SchemataProcess, new()
    where TToken : SchemataProcessToken, new()
    where TTransition : SchemataProcessTransition, new()
    where TCompensation : SchemataProcessCompensation, new()
    where TParticipant : SchemataProcessParticipant, new()
    where TSource : SchemataProcessSource, new()
{
    public async Task<SchemataProcess> HandleAsync(StartProcessRequest request, AdviceContext advice, CancellationToken ct = default) {
        var registration = support.ResolveRegistration(request.DefinitionName, request.Options?.DefinitionVersion ?? "1");
        var engine       = support.ResolveEngine(registration);
        var process      = FlowHandlerSupport<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>.NewProcess(registration, request.Options);
        ProcessSnapshot? snapshot = null;

        await support.ExecuteWithNotificationAsync(process, async (scope, current) => {
            await scope.CreateProcessAsync(process, current);
            await support.BindStartSourceAsync(
                scope,
                registration,
                process,
                request.Source,
                request.SourceType,
                request.SourceCanonicalName,
                current);
            var context = await support.CreateExecutionContextAsync(scope, process, request.Principal, advice, current);
            snapshot = await engine.StartAsync(registration.Definition, process, context, current);
            support.EnsureCatchesHaveHandlers(registration.Definition, snapshot);
            await support.RunAdvisorsAsync(
                registration, scope, context, snapshot, new Dictionary<string, string?>(), advice, current);
            await support.Persistence.PersistSnapshotAsync(scope, snapshot, current);
            if (support.Access is { } access) await access.RecordParticipationAsync(process, request.Principal, scope.UnitOfWork, current);
        }, ct);

        await support.Notifier.NotifyStartedAsync(snapshot!, ct);
        await support.Notifier.NotifyTransitionedAsync(snapshot!, ct);
        return process;
    }
}
