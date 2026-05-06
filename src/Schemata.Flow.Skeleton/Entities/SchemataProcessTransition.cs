using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;

namespace Schemata.Flow.Skeleton.Entities;

/// <summary>Represents a state transition within a process instance.</summary>
[DisplayName("Process Transition")]
[Table("SchemataProcessTransitions")]
[CanonicalName("processes/{process}/transitions/{transition}")]
public class SchemataProcessTransition : IIdentifier, ICanonicalName, ITransition, ITimestamp
{
    /// <summary>The canonical name of the process this transition belongs to.</summary>
    public virtual string? ProcessName { get; set; }

    /// <summary>The previous state before the transition.</summary>
    public virtual string? Previous { get; set; }

    /// <summary>The posterior state after the transition.</summary>
    public virtual string? Posterior { get; set; }

    #region ICanonicalName Members

    /// <summary>The human-readable name of the transition.</summary>
    public virtual string? Name { get; set; }

    /// <summary>The canonical resource name of the transition.</summary>
    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IIdentifier Members

    [TableKey]
    public virtual Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    /// <summary>The timestamp when the transition was created.</summary>
    public virtual DateTime? CreateTime { get; set; }

    /// <summary>The timestamp when the transition was last updated.</summary>
    public virtual DateTime? UpdateTime { get; set; }

    #endregion

    #region ITransition Members

    /// <summary>The event that triggered the transition.</summary>
    public virtual string Event { get; set; } = null!;

    /// <summary>An optional note describing the transition.</summary>
    public virtual string? Note { get; set; }

    /// <summary>The canonical resource name of the user who performed the transition.</summary>
    public virtual string? UpdatedByName { get; set; }

    #endregion
}
