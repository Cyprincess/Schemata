using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Scheduling.Skeleton.Entities;

/// <summary>
///     Long-running operation backing row. <see cref="IScheduler.TriggerAsync{TJob}" /> writes
///     the Pending row directly so the returned execution is immediately addressable; cron and
///     periodic fires upsert through <see cref="IJobLifecycleObserver" />. The public wire form
///     is <see cref="Schemata.Abstractions.Resource.Operation" /> and external callers see
///     read / list / delete only.
/// </summary>
[DisplayName("Operation")]
[Table("SchemataJobExecutions")]
[CanonicalName("operations/{operation}")]
[PrimaryKey(nameof(Uid))]
public class SchemataJobExecution : IIdentifier, ICanonicalName, IConcurrency, ISoftDelete, ITimestamp
{
    /// <summary>
    ///     AIP-122 canonical name of the originating <see cref="SchemataJob" />, or
    ///     <see langword="null" /> when this execution does not correspond to a persistent
    ///     scheduler entry (e.g. one-shot triggers raised by <see cref="IScheduler.TriggerAsync{TJob}" />
    ///     for back-channel logout, push dispatch, or resource purge).
    /// </summary>
    [ResourceReference(typeof(SchemataJob))]
    public virtual string? Job { get; set; }

    /// <summary>
    ///     The custom method verb that dispatched this execution as a long-running
    ///     operation (e.g. <c>purge</c>); <c>null</c> for ordinary cron / periodic fires.
    /// </summary>
    public virtual string? Method { get; set; }

    /// <summary>Stable job key resolving cron, periodic, one-time, and durable operation fires after a restart.</summary>
    public virtual string? JobKey { get; set; }

    /// <summary>Serialized typed arguments replayed by cron, periodic, one-time, and durable operation fires.</summary>
    public virtual string? ArgsJson { get; set; }

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
