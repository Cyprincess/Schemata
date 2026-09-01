using System.Collections.Generic;
using System.Threading;

namespace Schemata.Push.Skeleton;

/// <summary>
///     Broadcast fan-out facade. Immediate sends run through the request dispatcher and materialize
///     every transport result before this interface yields them. Deferred, durable delivery lives
///     in the Push Scheduling package via <c>IScheduledPushService</c>.
/// </summary>
public interface IPushService
{
    /// <summary>
    ///     Fans the dispatch out to every registered transport, waits for every transport to
    ///     finish, then yields the materialized results in transport-completion order.
    /// </summary>
    /// <param name="context">The dispatch context.</param>
    /// <param name="ct">A cancellation token.</param>
    IAsyncEnumerable<TransportResult> SendAsync(PushContext context, CancellationToken ct = default);
}
