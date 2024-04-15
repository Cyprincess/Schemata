using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Workflow.Skeleton.Models;

public class TransitionResponse : IIdentifier, IEvent, ITimestamp
{
    public virtual long WorkflowId { get; set; }

    public virtual string? Previous { get; set; }

    public virtual string? Posterior { get; set; }

    #region IEvent Members

    public virtual string Event { get; set; } = null!;

    public virtual string? Note { get; set; }

    public virtual long? UpdatedById { get; set; }

    public virtual string? UpdatedBy { get; set; }

    #endregion

    #region IIdentifier Members

    public virtual long Id { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
