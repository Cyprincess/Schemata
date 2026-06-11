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
    IScheduler            scheduler,
    OperationWorkRegistry registry
) : IOperationDispatcher
{
    private static readonly JsonSerializerOptions OutputOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<string> DispatchAsync<TResult>(
        string                                                    operation,
        Func<IServiceProvider, CancellationToken, Task<TResult?>> work,
        CancellationToken                                         ct
    )
        where TResult : class {
        var uid        = Guid.NewGuid();
        var collection = ResourceNameDescriptor.ForType<SchemataJobExecution>().Collection;

        // The typed result is serialized here, on the Scheduling side of the module
        // boundary, into the execution row's output so the operation resource can
        // surface it once the work completes.
        registry.Register(uid, async (sp, context, token) => {
            var result = await work(sp, token);
            if (result is not null && context.Execution is not null) {
                context.Execution.Output = JsonSerializer.Serialize(result, OutputOptions);
            }
        });

        try {
            var execution = await scheduler.TriggerAsync<OperationJob>(new() {
                Job          = $"{collection}/{uid:N}:{operation}",
                ExecutionUid = uid,
            }, ct);

            return execution.CanonicalName ?? $"{collection}/{uid:N}";
        } catch {
            registry.Remove(uid);
            throw;
        }
    }
}