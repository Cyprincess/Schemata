using System;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Workflow.Skeleton.Entities;

[Table("Transitions")]
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

    public virtual long Id { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreationDate { get; set; }

    public virtual DateTime? ModificationDate { get; set; }

    #endregion
}
