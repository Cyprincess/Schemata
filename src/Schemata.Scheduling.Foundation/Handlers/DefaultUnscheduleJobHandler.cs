using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Abstractions.Advisors;

namespace Schemata.Scheduling.Foundation.Handlers;

internal sealed class DefaultUnscheduleJobHandler<TJob, TExecution>(SchedulingHandlerSupport<TJob, TExecution> support, IServiceProvider services)
    : IRequestHandler<UnscheduleJobRequest, Unit>
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new()
{
    public async Task<Unit> HandleAsync(UnscheduleJobRequest request, AdviceContext advice, CancellationToken ct = default) {
        var scheduler = support.Scheduler;
        TJob? notified = null;

        await support.WriteGate.Gate.WaitAsync(ct);
        try {

            var context = await MessageContexts.CaptureAsync(services, ct);
            var scope = await scheduler.Services.GetRequiredService<IMessageExecutionScopeFactory>().CreateAsync(context, ct);
            IDisposable? entered = null;
            Exception? failure = null;
            try {
            entered = scope.Enter();
            await scope.RestoreAsync(context, ct);
            var jobs = scope.Services.GetRequiredService<IRepository<TJob>>();
            var executions = scope.Services.GetRequiredService<IRepository<TExecution>>();
            await using var transaction = jobs.Begin();
            executions.Join(transaction);
            var persisted = await jobs.FirstOrDefaultAsync(
                query => query.Where(job => job.CanonicalName == request.JobCanonicalName
                                          || job.Name == request.JobCanonicalName), ct);
            var canonical = persisted?.CanonicalName ?? request.JobCanonicalName;
            if (persisted is { State: JobState.Active }) {
                persisted.State = JobState.Paused;
                persisted.ScheduleVersion = Guid.NewGuid();
                if (await scope.Services.GetRequiredService<IResourceMutation<TJob>>()
                    .UpdateAsync(persisted, transaction, ct: ct) != MutationResult.Applied) return Unit.Value;
                notified = persisted;
            }
            await support.CancelPendingAsync(canonical, executions, scope.Services, transaction, ct);
            await transaction.CommitAsync(ct);
            await scheduler.Gate.WaitAsync(CancellationToken.None);
            try {
                if (scheduler.Entries.TryRemove(canonical, out var entry)) {
                    await entry.Cts.CancelAsync();
                    entry.Cts.Dispose();
                }
            } finally {
                scheduler.Gate.Release();
            }
            } catch (Exception error) {
                failure = error;
                throw;
            } finally {
                Exception? cleanup = null;
                try { await scope.DisposeAsync(); } catch (Exception error) { cleanup = error; }
                try { entered?.Dispose(); } catch (Exception error) { cleanup = cleanup is null ? error : new AggregateException(cleanup, error); }
                if (cleanup is not null) {
                    if (failure is not null) throw new AggregateException(failure, cleanup);
                    ExceptionDispatchInfo.Capture(cleanup).Throw();
                }
            }
        } finally {
            support.WriteGate.Gate.Release();
        }

        if (notified is not null) {
            await support.NotifyUnscheduledAsync(notified, ct);
        }

        return Unit.Value;
    }
}
