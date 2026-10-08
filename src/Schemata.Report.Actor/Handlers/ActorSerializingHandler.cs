using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Actor.Skeleton;
using Schemata.Messaging.Skeleton;
using Schemata.Report.Foundation;
using Schemata.Report.Skeleton;

namespace Schemata.Report.Actor.Handlers;

/// <summary>
///     Replaces the unkeyed default handler for a report-scoped command, redirecting a named
///     generation to the resolved report's actor so concurrent generations of the same report
///     serialize across canonical and leaf targets.
/// </summary>
/// <remarks>
///     The command pipeline checks Report selection before constructing this handler. Definition
///     lookup runs in the caller's scope before actor activation; the request keeps its original
///     name and only the resolved identity and flattened message context determine routing.
///     Inline requests resolve the keyed default handler in the caller's scope.
/// </remarks>
/// <typeparam name="TRequest">The report-scoped command type.</typeparam>
/// <typeparam name="TResult">The command's result type.</typeparam>
internal sealed class ActorSerializingHandler<TRequest, TResult>(
    IActorSystem actors, IServiceProvider caller) : IRequestHandler<TRequest, TResult>
    where TRequest : IRequest<TResult>, IReportScoped
{
    public async Task<TResult> HandleAsync(TRequest request, CancellationToken ct = default) {
        var key = request.ReportKey;
        if (string.IsNullOrWhiteSpace(key)) {
            return await caller.GetRequiredKeyedService<IRequestHandler<TRequest, TResult>>(
                              ReportConstants.Handlers.Default)
                          .HandleAsync(request, ct);
        }

        var context = MessageContexts.Capture(caller);
        var definition = await caller.GetRequiredService<IReportDefinitionStore>().ResolveAsync(key, ct);
        if (definition is { } resolved) {
            key = resolved.Report.CanonicalName ?? resolved.Report.Name ?? key;
        }
        var actor = await actors.GetAsync(new("report", key));
        return await actor.AskAsync<TRequest, TResult>(request, context, ct: ct);
    }
}