using System.Collections.Generic;
using System.Threading;

namespace Schemata.Push.Skeleton;

/// <summary>
///     Broadcast fan-out dispatch layer. <see cref="SendAsync" /> streams per-transport results
///     as each completes. Deferred, durable delivery lives in the Push Scheduling package via
///     <c>IScheduledPushService</c>.
/// </summary>
public interface IPushService
{
    /// <summary>
    ///     Fans the dispatch out to every registered transport concurrently and yields each
    ///     <see cref="TransportResult" /> as its transport completes, in completion order.
    /// </summary>
    /// <param name="context">The dispatch context.</param>
    /// <param name="ct">A cancellation token.</param>
    IAsyncEnumerable<TransportResult> SendAsync(PushContext context, CancellationToken ct = default);
}
