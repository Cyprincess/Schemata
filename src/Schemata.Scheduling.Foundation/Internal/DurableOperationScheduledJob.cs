using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Scheduling.Foundation.Internal;

public sealed class DurableOperationScheduledJob<TArgs>(IServiceProvider services) : IScheduledJob
    where TArgs : class
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
        var execution = context.Execution
                     ?? throw new InvalidOperationException("Operation execution row is missing.");

        if (string.IsNullOrEmpty(execution.JobKey)) {
            throw new InvalidOperationException("Operation execution is missing its durable operation descriptor.");
        }

        var args = execution.ArgsJson is { } json
            ? JsonSerializer.Deserialize<TArgs>(json, SchemataJson.Default)
            : null;

        var handler = services.GetRequiredKeyedService<IOperationHandler<TArgs>>(execution.JobKey);
        var result  = await handler.RunAsync(args!, ct);
        if (result is not null) {
            execution.Output = JsonSerializer.Serialize(result, SchemataJson.Default);
        }
    }
}
