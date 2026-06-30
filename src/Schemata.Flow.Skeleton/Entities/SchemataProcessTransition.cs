using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Entities;

/// <summary>A state transition within a process instance.</summary>
[DisplayName("Transition")]
[Table("SchemataProcessTransitions")]
[CanonicalName("processes/{process}/transitions/{transition}")]
[PrimaryKey(nameof(Uid))]
public class SchemataProcessTransition : IIdentifier, ICanonicalName, IConcurrency, ITransition, ITimestamp
{
    /// <summary>The parent process's <see cref="ICanonicalName.Name" />.</summary>
    public virtual string? Process { get; set; }

    /// <summary>
    ///     Full canonical name of the <see cref="SchemataProcessToken" /> this transition belongs to.
    ///     Equals the single token canonical under the state-machine engine; identifies the specific
    ///     token under the BPMN engine when multiple tokens are live.
    /// </summary>
    [ResourceReference(typeof(SchemataProcessToken))]
    public virtual string? Token { get; set; }

    /// <summary>
    ///     Classification of this transition. The state-machine engine writes only
    ///     <see cref="TransitionKind.Move" /> / <see cref="TransitionKind.Cancel" /> /
    ///     <see cref="TransitionKind.Fail" />; the BPMN engine additionally writes
    ///     <see cref="TransitionKind.Fork" /> / <see cref="TransitionKind.Join" /> /
    ///     <see cref="TransitionKind.Spawn" /> / <see cref="TransitionKind.Compensate" />.
    /// </summary>
    public virtual TransitionKind Kind { get; set; }

    /// <summary>The previous element <see cref="FlowElement.Name" /> before the transition.</summary>
    public virtual string? Previous { get; set; }

    /// <summary>The posterior element <see cref="FlowElement.Name" /> after the transition.</summary>
    public virtual string? Posterior { get; set; }

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

    #region ITransition Members

    public virtual string Event { get; set; } = null!;

    public virtual string? Note { get; set; }

    public virtual string? UpdatedBy { get; set; }

    #endregion
}
