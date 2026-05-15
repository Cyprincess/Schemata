using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;

namespace Schemata.Scheduling.Skeleton.Entities;

[DisplayName("Job")]
[Table("SchemataJobs")]
[CanonicalName("jobs/{job}")]
public class SchemataJob : IIdentifier, ICanonicalName, ITimestamp
{
    public virtual string? JobType { get; set; }

    public virtual ScheduleType ScheduleType { get; set; }

    public virtual DateTime? NextRunTime { get; set; }

    public virtual long? IntervalTicks { get; set; }

    public virtual string? CronExpression { get; set; }

    public virtual string? TimeZone { get; set; }

    public virtual string? Variables { get; set; }

    public virtual JobState State { get; set; }

    public virtual DateTime? LastRunTime { get; set; }

    public virtual string? LastError { get; set; }

    #region ICanonicalName Members

    public virtual string? Name { get; set; }

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IIdentifier Members

    [TableKey]
    public virtual Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
