using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Advice;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Advisors;

namespace Schemata.Push.Foundation;

/// <summary>
///     Broadcast fan-out <see cref="IPushService" />. Runs the <see cref="IPushSendAdvisor" />
///     pipeline, then invokes every registered <see cref="IPushTransport" /> concurrently,
///     yielding each <see cref="TransportResult" /> as its transport completes. A transport that
///     throws is isolated: its failure becomes a <see cref="TransportStatus.Failed" /> result while
///     the others proceed unaffected.
/// </summary>
public sealed class DefaultPushService : IPushService
{
    private readonly IServiceProvider _services;

    /// <summary>Initializes the push service against the current service scope.</summary>
    /// <param name="services">The service provider supplying transports and advisors.</param>
    public DefaultPushService(IServiceProvider services) { _services = services; }

    #region IPushService Members

    public async IAsyncEnumerable<TransportResult> SendAsync(
        PushContext                                context,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        var adviceContext = new AdviceContext(_services);
        var advice        = await Advisor.For<IPushSendAdvisor>().RunAsync(adviceContext, context, ct);
        if (advice is not AdviseResult.Continue) {
            yield break;
        }

        var pending = _services.GetServices<IPushTransport>()
                               .Select(transport => InvokeAsync(transport, context, ct))
                               .ToList();

        while (pending.Count > 0) {
            var finished = await Task.WhenAny(pending);
            pending.Remove(finished);
            yield return await finished;
        }
    }

    #endregion

    private static async Task<TransportResult> InvokeAsync(
        IPushTransport    transport,
        PushContext       context,
        CancellationToken ct
    ) {
        try {
            return await transport.TrySendAsync(context, ct);
        } catch (Exception ex) {
            return TransportResult.Failed(transport.Name, ex.Message);
        }
    }
}
