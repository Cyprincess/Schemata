using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;

namespace Schemata.Scheduling.Skeleton.Entities;

[DisplayName("Execution")]
[Table("SchemataJobExecutions")]
[CanonicalName("jobs/{job}/executions/{execution}")]
public class SchemataJobExecution : IIdentifier, ICanonicalName, ITimestamp
{
    public virtual string? JobName { get; set; }

    public virtual ExecutionState State { get; set; }

    public virtual DateTime StartTime { get; set; }

    public virtual DateTime? EndTime { get; set; }

    public virtual string? Error { get; set; }

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
