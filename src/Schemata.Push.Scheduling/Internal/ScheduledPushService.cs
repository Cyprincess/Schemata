using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Push.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Push.Scheduling.Internal;

/// <summary>
///     Default <see cref="IScheduledPushService" />. Persists the dispatch as a
///     <see cref="PushDispatchJob" /> through the scheduler and returns the pending
///     <see cref="Operation" /> envelope, so the deferred send is managed and observed through the
///     standard <c>operations/{operation}</c> long-running-operation surface.
/// </summary>
public sealed class ScheduledPushService : IScheduledPushService
{
    private const string SendMethod = "send";

    private readonly IScheduler _scheduler;

    /// <summary>Creates the scheduled push service.</summary>
    /// <param name="scheduler">The scheduler that runs the deferred dispatch job.</param>
    public ScheduledPushService(IScheduler scheduler) { _scheduler = scheduler; }

    #region IScheduledPushService Members

    public async ValueTask<Operation> ScheduleSendAsync(
        PushContext       context,
        DateTimeOffset?   at = null,
        CancellationToken ct = default
    ) {
        var argsJson   = JsonSerializer.Serialize(context, SchemataJson.Default);
        var uid        = Identifiers.NewUid();
        var collection = ResourceNameDescriptor.ForType<SchemataJobExecution>().Collection;

        var jobContext = new JobContext {
            Job          = $"{collection}/{uid:n}:{SendMethod}",
            ExecutionUid = uid,
            Method       = SendMethod,
            ArgsJson     = argsJson,
            StartTime    = at?.UtcDateTime,
        };

        var execution = await _scheduler.TriggerAsync<PushDispatchJob>(jobContext, ct);

        return OperationMapper.FromExecution(execution);
    }

    #endregion
}
