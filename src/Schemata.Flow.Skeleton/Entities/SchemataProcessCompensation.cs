using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Flow.Skeleton.Entities;

/// <summary>Persisted compensation binding registered by a process execution.</summary>
[Table("SchemataProcessCompensations")]
[PrimaryKey(nameof(Uid))]
[CanonicalName("process-compensations/{process_compensation}")]
public class SchemataProcessCompensation : IIdentifier, ICanonicalName, IConcurrency, ITimestamp
{
    [ConcurrencyCheck]
    public virtual Guid Timestamp { get; set; }

    /// <summary>Canonical name of the process that owns this compensation binding.</summary>
    public virtual string Process { get; set; } = null!; // EF Core materializes this required column.

    /// <summary>Canonical name of the scope that registered the compensating activity.</summary>
    public virtual string ScopeOwnerCanonicalName { get; set; } = null!; // EF Core materializes this required column.

    /// <summary>Name of the activity invoked when this binding is compensated.</summary>
    public virtual string ActivityName { get; set; } = null!; // EF Core materializes this required column.

    /// <summary>Graph name of the compensation boundary event this binding was registered for.</summary>
    public virtual string BoundaryName { get; set; } = null!; // EF Core materializes this required column.

    /// <summary>Stable registration order within the owning compensation scope.</summary>
    public virtual int RegistrationOrder { get; set; }

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
}
