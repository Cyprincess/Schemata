using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Internal;

public sealed partial class DefaultScheduler
{
    public async Task<SchemataJobExecution> TriggerAsync<TJob>(JobContext context, CancellationToken ct)
        where TJob : class, IScheduledJob {
        using var scope      = _services.CreateScope();
        var       dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        var inner = new TriggerJobRequest(context.Job ?? string.Empty, typeof(TJob), context);
        return await dispatcher.SendAsync<ResourceMethodRequest<SchemataJob, TriggerJobRequest, SchemataJobExecution>, SchemataJobExecution>(
            new(SchedulingOperations.Trigger, inner.JobCanonicalName, inner, null), ct);
    }

    public async Task RescheduleAsync(SchemataJob job, JobContext? preparedContext, CancellationToken ct) {
        using var scope      = _services.CreateScope();
        var       dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        await dispatcher.SendAsync<RescheduleJobRequest, Unit>(new(job, preparedContext), ct);
    }
}
