using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Actor.Foundation;
using Schemata.Actor.Skeleton;
using Schemata.Report.Foundation;
using Schemata.Report.Foundation.Commands;
using Schemata.Report.Skeleton;

namespace Schemata.Report.Actor.Handlers;

internal sealed class ReportActorRouting<TRequest> : IActorRouting<TRequest>
    where TRequest : IReportScoped
{
    public async ValueTask<ActorId?> ResolveAsync(TRequest request, IServiceProvider services, CancellationToken ct) {
        var key = request.ReportKey;
        if (string.IsNullOrWhiteSpace(key)) {
            return null;
        }

        var definition = await services.GetRequiredService<IReportDefinitionStore>().ResolveAsync(key, ct);
        if (definition is { } resolved) {
            key = resolved.Report.CanonicalName ?? resolved.Report.Name ?? key;
        }

        return new ActorId("report", key);
    }
}
