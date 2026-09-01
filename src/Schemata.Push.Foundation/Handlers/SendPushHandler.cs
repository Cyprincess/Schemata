using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Skeleton;

namespace Schemata.Push.Foundation.Handlers;

/// <summary>Runs the concurrent push transport fan-out.</summary>
public sealed class SendPushHandler(IServiceProvider services)
    : IRequestHandler<SendPushRequest, ImmutableArray<TransportResult>>
{
    public async Task<ImmutableArray<TransportResult>> HandleAsync(
        SendPushRequest   request,
        CancellationToken ct = default
    ) {
        _ = AdviceContext.Require();
        var pending = services.GetServices<IPushTransport>()
                              .Select(transport => InvokeAsync(transport, request.Context, ct))
                              .ToList();
        var results = ImmutableArray.CreateBuilder<TransportResult>(pending.Count);
        while (pending.Count > 0) {
            var finished = await Task.WhenAny(pending);
            pending.Remove(finished);
            results.Add(await finished);
        }

        return results.MoveToImmutable();
    }

    private static async Task<TransportResult> InvokeAsync(
        IPushTransport    transport,
        PushContext       context,
        CancellationToken ct
    ) {
        try {
            return await transport.TrySendAsync(context, ct);
        } catch (Exception exception) {
            return TransportResult.Failed(transport.Name, exception.Message);
        }
    }
}
