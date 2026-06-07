using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Scheduling.Skeleton.Entities;

/// <summary>
///     AIP-151 long-running operation backing row. Produced by the scheduler
///     during <see cref="IScheduler.TriggerAsync{TJob}" /> and during cron timer
///     fires; the wire form is exposed under <c>operations/{operation}</c>.
///     External callers see read / list / delete only; the scheduler writes
///     internally through <see cref="IJobLifecycleObserver" />.
/// </summary>
[DisplayName("Operation")]
[Table("SchemataJobExecutions")]
[CanonicalName("operations/{operation}")]
[Resource(typeof(SchemataJobExecution), Operations = [Operations.Get, Operations.List, Operations.Delete])]
[ResourceMethod("cancel", typeof(CancelOperationHandler))]
[ResourceMethod("wait",   typeof(WaitOperationHandler))]
[PrimaryKey(nameof(Uid))]
public class SchemataJobExecution : IIdentifier, ICanonicalName, ISoftDelete, ITimestamp
{
    /// <summary>Canonical name of the originating <see cref="SchemataJob" />.</summary>
    public virtual string? Job { get; set; }

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
