using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Runtime;

public sealed partial class DefaultScheduler<TJob, TExecution>
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new()
{
    public async Task<SchemataJobExecution> TriggerAsync<TJobType>(JobContext context, CancellationToken ct)
        where TJobType : class, IScheduledJob {
        var message = await MessageContexts.CaptureAsync(_services, ct);
        var runner = _services.GetRequiredService<IMessageExecutionScopeRunner>();
        return await runner.RunAsync(message, async (services, token) => {
            var dispatcher = services.GetRequiredService<IRequestDispatcher>();
            return await dispatcher.SendAsync<TriggerJobRequest<TExecution>, TExecution>(
                new(context.Job ?? string.Empty, typeof(TJobType), context), token);
        }, ct);
    }

    public async Task RescheduleAsync(TJob job, JobContext? preparedContext, CancellationToken ct) {
        var message = new MessageContext(job.MessageContext ?? new());
        var runner = _services.GetRequiredService<IMessageExecutionScopeRunner>();
        await runner.RunAsync(message, async (services, token) => {
            var dispatcher = services.GetRequiredService<IRequestDispatcher>();
            await dispatcher.SendAsync<RescheduleJobRequest<TJob>, Unit>(new(job, preparedContext), token);
            return Unit.Value;
        }, ct);
    }
}
