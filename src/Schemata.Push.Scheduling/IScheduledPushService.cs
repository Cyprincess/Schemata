using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Push.Skeleton;

namespace Schemata.Push.Scheduling;

/// <summary>
///     Deferred, durable push dispatch. <see cref="ScheduleSendAsync" /> persists the send through
///     the scheduler and returns its pending <see cref="Operation" /> envelope, so the dispatch
///     survives a host restart and is observed through the standard <c>operations/{operation}</c>
///     long-running-operation surface. Immediate dispatch stays on
///     <see cref="IPushService.SendAsync" />.
/// </summary>
public interface IScheduledPushService
{
    /// <summary>
    ///     Schedules the dispatch as a durable operation and returns its pending
    ///     <see cref="Operation" /> envelope.
    /// </summary>
    /// <param name="context">The dispatch context.</param>
    /// <param name="at">An optional absolute time to run the dispatch; <see langword="null" /> runs as soon as possible.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<Operation> ScheduleSendAsync(
        PushContext       context,
        DateTimeOffset?   at = null,
        CancellationToken ct = default
    );
}
