using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Runtime;

public sealed partial class DefaultScheduler<TJob, TExecution>
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new()
{
    public async Task ScheduleAsync(TJob job, CancellationToken ct) {
        var context = await MessageContexts.CaptureAsync(_services, ct);
        var runner = _services.GetRequiredService<IMessageExecutionScopeRunner>();
        await runner.RunAsync(context, async (services, token) => {
            var dispatcher = services.GetRequiredService<IRequestDispatcher>();
            await dispatcher.SendAsync<ScheduleJobRequest<TJob>, Unit>(new(job, null), token);
            return Unit.Value;
        }, ct);
    }

    public async Task ScheduleAsync(TJob job, IReadOnlyDictionary<string, string?>? variables, CancellationToken ct) {
        var context = await MessageContexts.CaptureAsync(_services, ct);
        var runner = _services.GetRequiredService<IMessageExecutionScopeRunner>();
        await runner.RunAsync(context, async (services, token) => {
            var dispatcher = services.GetRequiredService<IRequestDispatcher>();
            await dispatcher.SendAsync<ScheduleJobRequest<TJob>, Unit>(new(job, variables, ReplaceVariables: true), token);
            return Unit.Value;
        }, ct);
    }

    public async Task UnscheduleAsync(string jobCanonical, CancellationToken ct) {
        var context = await MessageContexts.CaptureAsync(_services, ct);
        var runner = _services.GetRequiredService<IMessageExecutionScopeRunner>();
        await runner.RunAsync(context, async (services, token) => {
            var dispatcher = services.GetRequiredService<IRequestDispatcher>();
            await dispatcher.SendAsync<UnscheduleJobRequest, Unit>(new(jobCanonical), token);
            return Unit.Value;
        }, ct);
    }
}
