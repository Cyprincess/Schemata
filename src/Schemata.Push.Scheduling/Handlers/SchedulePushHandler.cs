using Schemata.Push.Scheduling.Runtime;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Foundation.Commands;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Push.Scheduling.Handlers;

/// <summary>
///     Handles <see cref="SchedulePushRequest" /> by persisting the dispatch through
///     <see cref="IScheduler" /> as a durable <see cref="PushDispatchJob" /> execution. The
///     resulting <see cref="Operation" /> is returned so the scheduled send is observed through
///     the standard <c>operations/{operation}</c> long-running-operation surface.
/// </summary>
internal sealed class SchedulePushHandler(IScheduler scheduler) : IRequestHandler<SchedulePushRequest, Operation>
{
    private const string SendMethod = "send";

    public async Task<Operation> HandleAsync(SchedulePushRequest request, CancellationToken ct = default) {
        var argsJson = JsonSerializer.Serialize(request.Context, SchemataJson.Default);
        var uid      = Guid.NewGuid();

        // One-shot push dispatch has no persistent SchemataJob; the resulting
        // SchemataJobExecution is addressable as operations/{uid} on its own.
        var jobContext = new JobContext {
            ExecutionUid = uid,
            Method       = SendMethod,
            ArgsJson     = argsJson,
            StartTime    = request.At?.UtcDateTime,
        };

        var execution = await scheduler.TriggerAsync<PushDispatchJob>(jobContext, ct);

        return OperationMapper.FromExecution(execution);
    }
}