using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Actor.Skeleton;
using Schemata.Messaging.Skeleton;
using Schemata.Report.Foundation;
using Schemata.Report.Skeleton;

namespace Schemata.Report.Actor.Internal;

/// <summary>
///     Replaces the unkeyed default handler for a report-scoped command, redirecting a named
///     generation to the report's per-name actor so concurrent generations of the same report
///     serialize instead of racing on double snapshots and retention.
/// </summary>
/// <remarks>
///     Mirrors Flow.Actor's handler: constructed with only <see cref="IActorSystem" /> and the
///     caller's <see cref="IServiceProvider" /> — it never injects the keyed inner handler. The
///     caller's provider is read exactly once, synchronously, to capture the ambient
///     <see cref="MessageContext" />; only the request and the flattened context cross the mailbox
///     boundary. An inline request (<see cref="IReportScoped.ReportKey" /> is empty) carries no
///     report identity, so it bypasses the mailbox and resolves the keyed default handler directly
///     on the caller's own scope — no actor, exactly as without the bridge.
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
        var actor   = await actors.GetAsync(new ActorId("report", key));
        return await actor.AskAsync<TRequest, TResult>(request, context, ct: ct);
    }
}