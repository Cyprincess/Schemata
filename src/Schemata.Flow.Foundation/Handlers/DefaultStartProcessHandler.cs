using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Flow.Foundation.Handlers;

internal sealed class DefaultStartProcessHandler(FlowHandlerSupport support)
    : IRequestHandler<StartProcessRequest, SchemataProcess>
{
    public async Task<SchemataProcess> HandleAsync(StartProcessRequest request, CancellationToken ct = default) {
        var registration = support.ResolveRegistration(request.DefinitionName);
        var engine       = support.ResolveEngine(registration);
        var process      = FlowHandlerSupport.NewProcess(request.DefinitionName, request.Options);
        ProcessSnapshot? snapshot = null;

        await support.ExecuteWithNotificationAsync(process, async (scope, current) => {
            await support.BindStartSourceAsync(
                scope,
                registration,
                process,
                request.Source,
                request.SourceType,
                request.SourceCanonicalName,
                current);
            var context = await support.CreateExecutionContextAsync(scope, process, request.Principal, current);
            snapshot = await engine.StartAsync(registration.Definition, process, context, current);
            support.EnsureCatchesHaveHandlers(registration.Definition, snapshot);
            await support.RunAdvisorsAsync(
                registration, scope, context, snapshot, new Dictionary<string, string?>(), current);
            await support.Persistence.PersistSnapshotAsync(scope, snapshot, current);
        }, ct);

        await support.Notifier.NotifyStartedAsync(snapshot!, ct);
        await support.Notifier.NotifyTransitionedAsync(snapshot!, ct);
        return process;
    }
}
