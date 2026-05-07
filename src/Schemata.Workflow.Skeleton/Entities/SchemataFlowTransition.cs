using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;

namespace Schemata.Workflow.Skeleton.Entities;

/// <summary>
///     Records a single state transition within a workflow, capturing the event, previous and posterior states, and audit
///     metadata.
/// </summary>
[DisplayName("Transition")]
[Table("SchemataTransitions")]
[CanonicalName("workflows/{workflow}/transitions/{transition}")]
public class SchemataFlowTransition : IIdentifier, ICanonicalName, ITransition, ITimestamp
{
    /// <summary>
    ///     The identifier of the parent workflow this transition belongs to.
    /// </summary>
    public virtual Guid WorkflowId { get; set; }

    /// <summary>
    ///     The display name of the parent workflow.
    /// </summary>
    public virtual string? WorkflowName { get; set; }

    /// <summary>
    ///     The state before this transition occurred.
    /// </summary>
    public virtual string? Previous { get; set; }

    /// <summary>
    ///     The state after this transition occurred.
    /// </summary>
    public virtual string? Posterior { get; set; }

    #region ICanonicalName Members

    /// <inheritdoc />
    public virtual string? Name { get; set; }

    /// <inheritdoc />
    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IIdentifier Members

    /// <inheritdoc />
    [TableKey]
    public virtual Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    /// <inheritdoc />
    public virtual DateTime? CreateTime { get; set; }

    /// <inheritdoc />
    public virtual DateTime? UpdateTime { get; set; }

    #endregion

    #region ITransition Members

    /// <inheritdoc />
    public virtual string Event { get; set; } = null!;

    /// <inheritdoc />
    public virtual string? Note { get; set; }

    /// <inheritdoc />
    public virtual Guid? UpdatedById { get; set; }

    /// <inheritdoc />
    public virtual string? UpdatedBy { get; set; }

    #endregion
}
