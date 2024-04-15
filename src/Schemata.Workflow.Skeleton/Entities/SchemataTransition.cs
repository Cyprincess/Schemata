using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Workflow.Skeleton.Entities;

[DisplayName("Transition")]
[Table("SchemataTransitions")]
[CanonicalName("workflows/{workflow}/transitions/{transition}")]
public class SchemataTransition : IIdentifier, ICanonicalName, IEvent, ITimestamp
{
    public virtual long WorkflowId { get; set; }

    public virtual string? WorkflowName { get; set; }

    public virtual string? Previous { get; set; }

    public virtual string? Posterior { get; set; }

    #region ICanonicalName Members

    public virtual string? Name { get; set; }

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IEvent Members

    public virtual string Event { get; set; } = null!;

    public virtual string? Note { get; set; }

    public virtual long? UpdatedById { get; set; }

    public virtual string? UpdatedBy { get; set; }

    #endregion

    #region IIdentifier Members

    [Key]
    public virtual long Id { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
