using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Scheduling.Skeleton.Entities;

/// <summary>Persisted record for a scheduler entry, mirroring the in-memory registry.</summary>
[DisplayName("Job")]
[Table("SchemataJobs")]
[CanonicalName("jobs/{job}")]
[PrimaryKey(nameof(Uid))]
[Index(nameof(Name), IsUnique = true)]
public class SchemataJob : IIdentifier, ICanonicalName, IConcurrency, ITimestamp
{
    /// <summary>Stable job identifier resolved through <see cref="IScheduledJobRegistry" />.</summary>
    public virtual string? JobKey { get; set; }

    /// <summary>Discriminator for the schedule kind.</summary>
    public virtual ScheduleType ScheduleType { get; set; }

    /// <summary>Next computed fire time.  <c>null</c> for terminal states.</summary>
    public virtual DateTime? NextRunTime { get; set; }

    /// <summary>Interval ticks for <see cref="Entities.ScheduleType.Periodic" /> entries.</summary>
    public virtual long? IntervalTicks { get; set; }

    /// <summary>
    ///     Anchor for <see cref="Entities.ScheduleType.Periodic" /> entries, preserving the
    ///     <c>StartTime</c> so interval boundaries survive a round-trip through persistence.
    /// </summary>
    public virtual DateTime? AnchorTime { get; set; }

    /// <summary>Cron expression for <see cref="Entities.ScheduleType.Cron" /> entries.</summary>
    public virtual string? CronExpression { get; set; }

    /// <summary>Serialized typed argument payload consumed by the job body through <see cref="JobContext.ArgsJson" />.</summary>
    public virtual string? ArgsJson { get; set; }

    /// <summary>Free-form string variables mirrored to <see cref="JobContext.Variables" />; persisted as a provider-managed JSON column.</summary>
    public virtual Dictionary<string, string?>? Variables { get; set; }

    /// <summary>
    ///     Whether the scheduler may re-fire this job after a missed window or
    ///     a host restart.  <see cref="IScheduler.TriggerAsync{TJob}" /> sets
    ///     this to <c>false</c> for fire-once audit semantics.
    /// </summary>
    public virtual bool Replay { get; set; } = true;

    /// <summary>Lifecycle state of this entry.</summary>
    public virtual JobState State { get; set; }

    /// <summary>Wall-clock time of the most recent fire.</summary>
    public virtual DateTime? RecentRunTime { get; set; }

    /// <summary>Diagnostic message from the most recent failed fire.</summary>
    public virtual string? RecentError { get; set; }

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

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
