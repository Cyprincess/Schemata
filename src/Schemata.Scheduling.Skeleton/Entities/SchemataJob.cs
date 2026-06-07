using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Microsoft.EntityFrameworkCore;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Scheduling.Skeleton.Entities;

/// <summary>Persisted record for a scheduler entry, mirroring the in-memory registry.</summary>
[DisplayName("Job")]
[Table("SchemataJobs")]
[CanonicalName("jobs/{job}")]
[Resource(typeof(SchemataJob))]
[ResourceMethod("run", typeof(RunJobHandler))]
[PrimaryKey(nameof(Uid))]
public class SchemataJob : IIdentifier, ICanonicalName, IConcurrency, ITimestamp
{
    /// <summary>Assembly-qualified <see cref="IScheduledJob" /> type the scheduler will resolve and run.</summary>
    public virtual string? JobType { get; set; }

    /// <summary>Discriminator for the schedule kind.</summary>
    public virtual ScheduleType ScheduleType { get; set; }

    /// <summary>Next computed fire time.  <c>null</c> for terminal states.</summary>
    public virtual DateTime? NextRunTime { get; set; }

    /// <summary>Interval ticks for <see cref="Entities.ScheduleType.Periodic" /> entries.</summary>
    public virtual long? IntervalTicks { get; set; }

    /// <summary>Cron expression for <see cref="Entities.ScheduleType.Cron" /> entries.</summary>
    public virtual string? CronExpression { get; set; }

    /// <summary>Serialized <see cref="JobContext.Variables" /> for the next fire.</summary>
    public virtual string? Variables { get; set; }

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

    public virtual Guid? Timestamp { get; set; }

    #endregion

    #region IIdentifier Members
    public virtual Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
