using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;

namespace Schemata.Flow.Skeleton.Entities;

/// <summary>A state transition within a process instance.</summary>
[DisplayName("Transition")]
[Table("SchemataProcessTransitions")]
[CanonicalName("processes/{process}/transitions/{transition}")]
[PrimaryKey(nameof(Uid))]
public class SchemataProcessTransition : IIdentifier, ICanonicalName, ITransition, ITimestamp
{
    /// <summary>The parent process's <see cref="ICanonicalName.Name" />.</summary>
    public virtual string? Process { get; set; }

    /// <summary>The previous state before the transition.</summary>
    public virtual string? Previous { get; set; }

    /// <summary>The posterior state after the transition.</summary>
    public virtual string? Posterior { get; set; }

    #region ICanonicalName Members

    public virtual string? Name { get; set; }

    public virtual string? CanonicalName { get; set; }

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
