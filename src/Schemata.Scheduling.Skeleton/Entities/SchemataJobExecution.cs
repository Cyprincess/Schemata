using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Scheduling.Skeleton.Entities;

/// <summary>
///     Durable execution backing row. Its purpose is framework-internal reliable scheduling and
///     audit: <see cref="IScheduler.TriggerAsync{TJob}" /> writes the Pending row directly so the
///     returned execution is immediately addressable, cron and periodic fires upsert through
///     <see cref="IJobLifecycleObserver" />, and the execution-store provider contract
///     (<see cref="IJobExecutionStore" />) carries the lease, retry-ceiling, and crash-recovery
///     semantics of the dispatch lifecycle. The public wire form is
///     <see cref="Schemata.Abstractions.Resource.Operation" /> and external callers see
///     read / list / delete only.
/// </summary>
[Table("SchemataJobExecutions")]
[CanonicalName("operations/{operation}")]
[PrimaryKey(nameof(Uid))]
public class SchemataJobExecution : IIdentifier, ICanonicalName, IConcurrency, ISoftDelete, ITimestamp
{
    public virtual string Tenant { get; set; } = "host";

    /// <summary>
    ///     AIP-122 canonical name of the originating <see cref="SchemataJob" />, or
    ///     <see langword="null" /> when this execution does not correspond to a persistent
    ///     scheduler entry (e.g. one-shot triggers raised by <see cref="IScheduler.TriggerAsync{TJob}" />
    ///     for back-channel logout, push dispatch, or resource purge).
    /// </summary>
    [ResourceReference(typeof(SchemataJob))]
    public virtual string? Job { get; set; }

    public virtual Guid ScheduleVersion { get; set; }

    /// <summary>
    ///     The custom method verb that dispatched this execution as a long-running
    ///     operation (e.g. <c>purge</c>); <c>null</c> for ordinary cron / periodic fires.
    /// </summary>
    public virtual string? Method { get; set; }

    /// <summary>Stable job key resolving cron, periodic, one-time, and durable operation fires after a restart.</summary>
    public virtual string? JobKey { get; set; }

    /// <summary>Serialized typed arguments replayed by cron, periodic, one-time, and durable operation fires.</summary>
    public virtual string? ArgsJson { get; set; }

    /// <summary>
    ///     Free-form string variables carried to the dispatched job through
    ///     <see cref="JobContext.Variables" />. Copied from the trigger context (one-shot fires)
    ///     or the <see cref="SchemataJob.Variables" /> row (durable fires) when the Pending row is
    ///     materialized, so the dispatch path replays them after a restart. Provider-managed JSON column.
    /// </summary>
    public virtual Dictionary<string, string?>? Variables { get; set; }

    /// <summary>Lifecycle state of this execution.</summary>
    public virtual ExecutionState State { get; set; }

    /// <summary>Wall-clock start time recorded by the scheduler at trigger.</summary>
    public virtual DateTime StartTime { get; set; }

    /// <summary>Wall-clock end time, set when the execution finishes.</summary>
    public virtual DateTime? EndTime { get; set; }

    /// <summary>Diagnostic message captured on failure.</summary>
    public virtual string? RecentError { get; set; }

    /// <summary>
    ///     Serialized result document produced by the job body (the AIP-151
    ///     <c>response</c> payload). Jobs assign it through
    ///     <see cref="JobContext.Execution" /> before completing; the audit
    ///     observer persists it alongside the terminal state.
    /// </summary>
    public virtual string? Output { get; set; }

    /// <summary>
    ///     Number of times this execution has been claimed. The store increments it atomically
    ///     with every claim, including crash-recovery reclaims, so the retry ceiling configured on
    ///     the job registration keeps counting across attempts.
    /// </summary>
    public virtual int Attempt { get; set; }

    /// <summary>
    ///     Instant at which the current claim lapses. The dispatcher renews it while the job body
    ///     runs; once it passes without renewal (host shutdown or crash), the row returns to the
    ///     pending set and any dispatcher may reclaim it. <see langword="null" /> when no
    ///     dispatcher holds the row, including Running rows owned by an in-process
    ///     long-running-operation client, which are never claimable.
    /// </summary>
    public virtual DateTime? LeaseExpireTime { get; set; }

    #region ICanonicalName Members

    public virtual string? Name { get; set; }

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    [ConcurrencyCheck]
    public virtual Guid Timestamp { get; set; }

    #endregion

    #region IIdentifier Members
    public virtual Guid Uid { get; set; }

    #endregion

    #region ISoftDelete Members

    public virtual DateTime? DeleteTime { get; set; }

    public virtual DateTime? PurgeTime { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
