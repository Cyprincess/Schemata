using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Internal;

internal sealed class SchedulerOperationDispatcher(
    IScheduler         scheduler,
    IOperationRegistry registry,
    IScheduledJobRegistry jobs
) : IOperationDispatcher
{
    public async Task<Operation> DispatchAsync<TArgs>(
        string            operationKey,
        TArgs             args,
        CancellationToken ct
    )
        where TArgs : class {
        var descriptor = registry.GetRequired(operationKey);
        if (descriptor.ArgsType != typeof(TArgs)) {
            throw new InvalidOperationException(
                $"Operation '{operationKey}' expects arguments of type '{descriptor.ArgsType}', but got '{typeof(TArgs)}'.");
        }

        // Persist the operation key and serialized arguments instead of an in-process
        // closure, so a reloaded execution can rebuild and run the handler after a restart.
        var argsJson   = JsonSerializer.Serialize(args, SchemataJson.Default);
        var uid        = Identifiers.NewUid();
        var collection = ResourceNameDescriptor.ForType<SchemataJobExecution>().Collection;

        jobs.Register(typeof(DurableOperationScheduledJob<TArgs>), operationKey);

        var execution = await scheduler.TriggerAsync<DurableOperationScheduledJob<TArgs>>(new() {
            Job               = $"{collection}/{uid:n}:{descriptor.Method}",
            ExecutionUid      = uid,
            Method            = descriptor.Method,
            JobKey            = operationKey,
            ArgsJson          = argsJson,
        }, ct);

        return OperationMapper.FromExecution(execution);
    }
}
