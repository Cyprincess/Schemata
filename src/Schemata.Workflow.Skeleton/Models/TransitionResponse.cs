using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Workflow.Skeleton.Models;

/// <summary>
///     Response DTO for a single workflow transition record.
/// </summary>
public class TransitionResponse : IIdentifier, ITransition, ITimestamp
{
    /// <summary>
    ///     The identifier of the parent workflow.
    /// </summary>
    public virtual long WorkflowId { get; set; }

    /// <summary>
    ///     The state before this transition.
    /// </summary>
    public virtual string? Previous { get; set; }

    /// <summary>
    ///     The state after this transition.
    /// </summary>
    public virtual string? Posterior { get; set; }

    #region IIdentifier Members

    /// <inheritdoc />
    public virtual long Id { get; set; }

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
    public virtual long? UpdatedById { get; set; }

    /// <inheritdoc />
    public virtual string? UpdatedBy { get; set; }

    #endregion
}
