using System;
using System.Collections.Generic;
using Schemata.Abstractions.Entities;

namespace Schemata.Workflow.Skeleton.Models;

/// <summary>
/// Response DTO for a workflow, including its current state, state graph, available events, and transition history.
/// </summary>
public class WorkflowResponse : IIdentifier, IStateful, ITimestamp
{
    /// <summary>
    /// The state graph showing all states and transitions.
    /// </summary>
    public virtual GraphResponse? Graph { get; set; }

    /// <summary>
    /// The names of events available from the current state.
    /// </summary>
    public virtual List<string>? Events { get; set; }

    /// <summary>
    /// The recorded transition history.
    /// </summary>
    public virtual List<TransitionResponse>? Transitions { get; set; }

    #region IIdentifier Members

    /// <inheritdoc />
    public virtual long Id { get; set; }

    #endregion

    #region IStateful Members

    /// <inheritdoc />
    public virtual string? State { get; set; }

    #endregion

    #region ITimestamp Members

    /// <inheritdoc />
    public virtual DateTime? CreateTime { get; set; }

    /// <inheritdoc />
    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
