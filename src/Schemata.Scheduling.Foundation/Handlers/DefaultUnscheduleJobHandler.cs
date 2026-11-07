using System;
using System.Linq;
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
            var runner = scheduler.Services.GetRequiredService<IMessageExecutionScopeRunner>();
            Func<IServiceProvider, CancellationToken, ValueTask<Unit>> work = async (provider, token) => {
            var jobs = provider.GetRequiredService<IRepository<TJob>>();
            var executions = provider.GetRequiredService<IRepository<TExecution>>();
            IUnitOfWork transaction;
            IUnitOfWork? owned = null;
            if (request.UnitOfWork is not null) {
                transaction = request.UnitOfWork;
                jobs.Join(transaction);
            } else {
                owned = jobs.Begin();
                transaction = owned;
            }
            await using var ownedTransaction = owned;
            executions.Join(transaction);
            var persisted = await jobs.FirstOrDefaultAsync(
                query => query.Where(job => job.CanonicalName == request.JobCanonicalName
                                          || job.Name == request.JobCanonicalName), ct);
            var canonical = persisted?.CanonicalName ?? request.JobCanonicalName;
            if (persisted is { State: JobState.Active }) {
                persisted.State = JobState.Paused;
                persisted.ScheduleVersion = Guid.NewGuid();
                if (await provider.GetRequiredService<IResourceMutation<TJob>>()
                    .UpdateAsync(persisted, transaction, ct: ct) != MutationResult.Applied) return Unit.Value;
                notified = persisted;
            }
            await support.CancelPendingAsync(canonical, executions, provider, transaction, ct);
            if (ownedTransaction is not null) {
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
            } else {
                transaction.AddCommitSink(CommitOrders.Domain, async token => {
                    await scheduler.Gate.WaitAsync(CancellationToken.None);
                    try {
                        if (scheduler.Entries.TryRemove(canonical, out var entry)) {
                            await entry.Cts.CancelAsync();
                            entry.Cts.Dispose();
                        }
                    } finally {
                        scheduler.Gate.Release();
                    }
                    if (notified is not null) {
                        await support.NotifyUnscheduledAsync(notified, token);
                        notified = null;
                    }
                });
            }
            return Unit.Value;
            };
            if (request.UnitOfWork is not null) {
                await work(services, ct);
            } else {
                await runner.RunAsync(context, work, ct);
            }
        } finally {
            support.WriteGate.Gate.Release();
        }

        if (notified is not null && request.UnitOfWork is null) {
            await support.NotifyUnscheduledAsync(notified, ct);
        }

        return Unit.Value;
    }
}
