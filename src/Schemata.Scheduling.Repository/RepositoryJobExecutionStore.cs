using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Repository;

/// <summary>
///     Default <see cref="IJobExecutionStore{TJob,TExecution}" /> over the Schemata repository
///     persistence (<see cref="IRepository{TEntity}" />). Every transition is a single guarded
///     write: the entity's concurrency stamp serializes competing dispatchers, and a lost race
///     surfaces as <see cref="AbortedException" /> from the store, which the transition reports
///     as <see langword="false" />. Atomicity comes from the backing store whatever engine the
///     application installed for <typeparamref name="TExecution" />; nothing is serialized
///     in-process.
/// </summary>
/// <typeparam name="TJob">The persisted job entity type registered by the host.</typeparam>
/// <typeparam name="TExecution">The persisted execution entity type registered by the host.</typeparam>
public sealed class RepositoryJobExecutionStore<TJob, TExecution>(
    IRepository<TExecution> executions,
    IServiceProvider services
) : IJobExecutionStore<TJob, TExecution>
    where TJob : SchemataJob
    where TExecution : SchemataJobExecution
{
    #region IJobExecutionStore Members
    public IAsyncEnumerable<TJob> ListActiveJobsAsync(CancellationToken ct) =>
        services.GetRequiredService<IRepository<TJob>>().ListAsync(query => query.Where(job => job.State == JobState.Active), ct);


    public async IAsyncEnumerable<TExecution> ListDueAsync(
        DateTime                                     asOfUtc,
        int                                          batchSize,
        [EnumeratorCancellation] CancellationToken   ct
    ) {
        await foreach (var row in executions.ListAsync(
                           q => q.Where(e => (e.State == ExecutionState.Pending && e.StartTime <= asOfUtc)
                                          || (e.State == ExecutionState.Running && e.JobKey != null && e.LeaseExpireTime != null
                                           && e.LeaseExpireTime <= asOfUtc))
                                 .Take(batchSize),
                           ct)) {
            yield return row;
        }
    }

    public async Task<bool> TryClaimAsync(
        TExecution        execution,
        DateTime          leaseExpireUtc,
        CancellationToken ct
    ) {
        execution.State           = ExecutionState.Running;
        execution.LeaseExpireTime = leaseExpireUtc;
        execution.Attempt++;

        return await CommitAsync(execution, ct);
    }

    public async Task<bool> TryRenewLeaseAsync(
        TExecution        execution,
        DateTime          leaseExpireUtc,
        CancellationToken ct
    ) {
        execution.LeaseExpireTime = leaseExpireUtc;

        return await CommitAsync(execution, ct);
    }

    public async Task<bool> TrySettleAsync(TExecution execution, CancellationToken ct) {
        execution.LeaseExpireTime = null;

        return await CommitAsync(execution, ct);
    }

    public async Task<bool> TryRequeueAsync(
        TExecution        execution,
        DateTime          nextStartUtc,
        CancellationToken ct
    ) {
        execution.State           = ExecutionState.Pending;
        execution.StartTime       = nextStartUtc;
        execution.LeaseExpireTime = null;

        return await CommitAsync(execution, ct);
    }

    public async Task FailOrphanedRunningAsync(DateTime failedAtUtc, CancellationToken ct) {
        var orphaned = new List<TExecution>();
        await foreach (var row in executions.ListAsync(
                           q => q.Where(e => e.State == ExecutionState.Running && e.JobKey == null
                               && e.LeaseExpireTime != null && e.LeaseExpireTime <= failedAtUtc), ct)) {
            orphaned.Add(row);
        }

        if (orphaned.Count == 0) {
            return;
        }

        foreach (var row in orphaned) {
            var context = new MessageContext(row.MessageContext ?? new());
            var scope = await services.GetRequiredService<IMessageExecutionScopeFactory>().CreateAsync(context, ct);
            IDisposable? entered = null;
            Exception? failure = null;
            try {
                entered = scope.Enter();
                await scope.RestoreAsync(context, ct);
                row.State = ExecutionState.Failed;
                row.EndTime = failedAtUtc;
                row.RecentError = "Inline execution ownership expired before completion.";
                await scope.Services.GetRequiredService<IJobExecutionStore<TJob, TExecution>>().TrySettleAsync(row, ct);
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
        }

    }

    #endregion

    private async Task<bool> CommitAsync(TExecution execution, CancellationToken ct) {
        try {
            if (await executions.UpdateAsync(execution, ct) != MutationResult.Applied)
                throw new FailedPreconditionException("Required execution transition was not applied.");
            await executions.CommitAsync(ct);
            return true;
        } catch (AbortedException) {
            return false;
        }
    }
}
