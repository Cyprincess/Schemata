using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Repository;

/// <summary>
///     Default <see cref="IJobExecutionStore" /> over the Schemata repository persistence
///     (<see cref="IRepository{TEntity}" />). Every transition is a single guarded write: the
///     entity's concurrency stamp serializes competing dispatchers, and a lost race surfaces as
///     <see cref="AbortedException" /> from the store, which the transition reports as
///     <see langword="false" />. Atomicity comes from the backing store whatever engine the
///     application installed for <see cref="SchemataJobExecution" />; nothing is serialized
///     in-process.
/// </summary>
public sealed class RepositoryJobExecutionStore(
    IRepository<SchemataJobExecution>       executions,
    IResourceMutation<SchemataJobExecution> mutation
) : IJobExecutionStore
{
    #region IJobExecutionStore Members

    public async IAsyncEnumerable<SchemataJobExecution> ListDueAsync(
        DateTime                                     asOfUtc,
        int                                          batchSize,
        [EnumeratorCancellation] CancellationToken   ct
    ) {
        await foreach (var row in executions.ListAsync(
                           q => q.Where(e => (e.State == ExecutionState.Pending && e.StartTime <= asOfUtc)
                                          || (e.State == ExecutionState.Running && e.LeaseExpireTime != null
                                           && e.LeaseExpireTime <= asOfUtc))
                                 .Take(batchSize),
                           ct)) {
            yield return row;
        }
    }

    public async Task<bool> TryClaimAsync(
        SchemataJobExecution execution,
        DateTime             leaseExpireUtc,
        CancellationToken    ct
    ) {
        execution.State           = ExecutionState.Running;
        execution.LeaseExpireTime = leaseExpireUtc;
        execution.Attempt++;

        return await CommitAsync(execution, ct);
    }

    public async Task<bool> TryRenewLeaseAsync(
        SchemataJobExecution execution,
        DateTime             leaseExpireUtc,
        CancellationToken    ct
    ) {
        execution.LeaseExpireTime = leaseExpireUtc;

        return await CommitAsync(execution, ct);
    }

    public async Task<bool> TrySettleAsync(SchemataJobExecution execution, CancellationToken ct) {
        execution.LeaseExpireTime = null;

        return await CommitAsync(execution, ct);
    }

    public async Task<bool> TryRequeueAsync(
        SchemataJobExecution execution,
        DateTime             nextStartUtc,
        CancellationToken    ct
    ) {
        execution.State           = ExecutionState.Pending;
        execution.StartTime       = nextStartUtc;
        execution.LeaseExpireTime = null;

        return await CommitAsync(execution, ct);
    }

    public async Task FailOrphanedRunningAsync(DateTime failedAtUtc, CancellationToken ct) {
        var orphaned = new List<SchemataJobExecution>();
        await foreach (var row in executions.ListAsync(
                           q => q.Where(e => e.State == ExecutionState.Running && e.LeaseExpireTime == null), ct)) {
            orphaned.Add(row);
        }

        if (orphaned.Count == 0) {
            return;
        }

        await using var unit = executions.Begin();
        foreach (var row in orphaned) {
            row.State       = ExecutionState.Failed;
            row.EndTime     = failedAtUtc;
            row.RecentError = "Execution was interrupted by a host restart.";
            await mutation.UpdateAsync(row, unit, ct: ct);
        }

        await unit.CommitAsync(ct);
    }

    #endregion

    private async Task<bool> CommitAsync(SchemataJobExecution execution, CancellationToken ct) {
        try {
            await executions.UpdateAsync(execution, ct);
            await executions.CommitAsync(ct);
            return true;
        } catch (AbortedException) {
            return false;
        }
    }
}
