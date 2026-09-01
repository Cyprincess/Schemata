using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Skeleton;

namespace Schemata.Push.Scheduling.Internal;

/// <summary>
///     Default <see cref="IScheduledPushService" />. Forwards to <see cref="SchedulePushHandler" />
///     through <see cref="IRequestDispatcher" /> so the dispatch is persisted as a
///     <see cref="PushDispatchJob" /> and observed through the standard
///     <c>operations/{operation}</c> long-running-operation surface.
/// </summary>
public sealed class ScheduledPushService(IRequestDispatcher dispatcher) : IScheduledPushService
{
    #region IScheduledPushService Members

    public ValueTask<Operation> ScheduleSendAsync(
        PushContext       context,
        DateTimeOffset?   at      = null,
        CancellationToken ct      = default
    ) => new(dispatcher.SendAsync<SchedulePushRequest, Operation>(new(context, at), ct));

    #endregion
}