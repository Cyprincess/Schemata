using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     Execution-store provider contract for durable job execution. The Scheduling core consumes
///     this seam; backends implement it, and an application replaces the backend by installing a
///     different provider through dependency injection without touching job registrations or
///     handlers. The <see cref="SchemataJobExecution" /> entity exists for framework-internal
///     reliable scheduling and audit; it is not a general-purpose business inbox/outbox.
/// </summary>
/// <remarks>
///     <para>
///         The pending set comprises <see cref="ExecutionState.Pending" /> rows whose
///         <see cref="SchemataJobExecution.StartTime" /> has come due, plus
///         <see cref="ExecutionState.Running" /> rows whose lease expired. Lease expiry returns an
///         execution to the pending set, which is the crash-recovery path: a dispatcher that dies
///         mid-run stops renewing, the lease lapses, and any live dispatcher may claim the row
///         again. Rows owned by an in-process long-running-operation client carry no lease and are
///         never claimable.
///     </para>
///     <para>
///         Every write is guarded by the row's concurrency token. Implementations provide real
///         atomicity from the backing store; a lost race reports <see langword="false" /> rather
///         than overwriting a row another worker transitioned. In-process locks are not a
///         substitute for cross-process atomicity.
///     </para>
/// </remarks>
public interface IJobExecutionStore
{
    /// <summary>
    ///     Lists the claimable execution rows: due <see cref="ExecutionState.Pending" /> rows and
    ///     <see cref="ExecutionState.Running" /> rows whose
    ///     <see cref="SchemataJobExecution.LeaseExpireTime" /> is at or before
    ///     <paramref name="asOfUtc" />.
    /// </summary>
    /// <param name="asOfUtc">Instant against which due times and lease expiries are compared.</param>
    /// <param name="batchSize">Maximum number of rows to yield.</param>
    /// <param name="ct">Cancellation token.</param>
    IAsyncEnumerable<SchemataJobExecution> ListDueAsync(DateTime asOfUtc, int batchSize, CancellationToken ct);

    /// <summary>
    ///     Atomically claims <paramref name="execution" /> for this dispatcher: the row transitions
    ///     to <see cref="ExecutionState.Running" />, <see cref="SchemataJobExecution.Attempt" />
    ///     increments, and <see cref="SchemataJobExecution.LeaseExpireTime" /> is set to
    ///     <paramref name="leaseExpireUtc" />. Reclaiming a lease-expired Running row uses the same
    ///     transition.
    /// </summary>
    /// <param name="execution">The due row read from <see cref="ListDueAsync" />, carrying its expected concurrency token.</param>
    /// <param name="leaseExpireUtc">Instant at which the claim lapses unless renewed or settled.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///     <see langword="true" /> when the claim committed; <see langword="false" /> when another
    ///     claimant transitioned the row first.
    /// </returns>
    Task<bool> TryClaimAsync(SchemataJobExecution execution, DateTime leaseExpireUtc, CancellationToken ct);

    /// <summary>
    ///     Renews the lease of a row this dispatcher claimed, moving
    ///     <see cref="SchemataJobExecution.LeaseExpireTime" /> to <paramref name="leaseExpireUtc" />
    ///     so a long job body outlives the original claim.
    /// </summary>
    /// <param name="execution">The claimed row, carrying its expected concurrency token.</param>
    /// <param name="leaseExpireUtc">The new lease expiry.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///     <see langword="true" /> when the renewal committed; <see langword="false" /> when the
    ///     row moved on under another worker (for example a <c>:cancel</c>), in which case the
    ///     caller stops the job body and honours the other transition.
    /// </returns>
    Task<bool> TryRenewLeaseAsync(SchemataJobExecution execution, DateTime leaseExpireUtc, CancellationToken ct);

    /// <summary>
    ///     Persists the terminal state the caller assigned to <paramref name="execution" />
    ///     (<see cref="ExecutionState.Succeeded" />, <see cref="ExecutionState.Failed" />,
    ///     <see cref="ExecutionState.Blocked" />, or <see cref="ExecutionState.Skipped" />) and
    ///     clears <see cref="SchemataJobExecution.LeaseExpireTime" />.
    /// </summary>
    /// <param name="execution">The claimed row with its terminal fields assigned, carrying its expected concurrency token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///     <see langword="true" /> when the settlement committed; <see langword="false" /> when the
    ///     row moved on under another worker, in which case the caller honours that transition.
    /// </returns>
    Task<bool> TrySettleAsync(SchemataJobExecution execution, CancellationToken ct);

    /// <summary>
    ///     Returns a failed execution to the pending set for another attempt: the row transitions
    ///     to <see cref="ExecutionState.Pending" /> with <see cref="SchemataJobExecution.StartTime" />
    ///     set to <paramref name="nextStartUtc" /> and the lease cleared, while
    ///     <see cref="SchemataJobExecution.Attempt" /> is retained so the retry ceiling keeps
    ///     counting across attempts.
    /// </summary>
    /// <param name="execution">The claimed row, carrying its expected concurrency token.</param>
    /// <param name="nextStartUtc">Backoff-adjusted instant at which the row becomes due again.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///     <see langword="true" /> when the requeue committed; <see langword="false" /> when the
    ///     row moved on under another worker, in which case the caller honours that transition.
    /// </returns>
    Task<bool> TryRequeueAsync(SchemataJobExecution execution, DateTime nextStartUtc, CancellationToken ct);

    /// <summary>
    ///     Crash recovery for executions left <see cref="ExecutionState.Running" /> without a lease.
    ///     Lease-less Running rows belong to in-process long-running-operation clients, so any of them
    ///     observed after a restart was orphaned by the interrupted process: the implementation
    ///     settles every such row <see cref="ExecutionState.Failed" /> with
    ///     <see cref="SchemataJobExecution.EndTime" /> set to <paramref name="failedAtUtc" /> and a
    ///     restart-interruption error recorded in <see cref="SchemataJobExecution.RecentError" />.
    ///     Dispatcher-claimed rows carry a lease and recover through the pending set instead, so they
    ///     are never touched here. Writes remain guarded by each row's concurrency token.
    /// </summary>
    /// <param name="failedAtUtc">Instant recorded as <see cref="SchemataJobExecution.EndTime" />.</param>
    /// <param name="ct">Cancellation token.</param>
    Task FailOrphanedRunningAsync(DateTime failedAtUtc, CancellationToken ct);
}
