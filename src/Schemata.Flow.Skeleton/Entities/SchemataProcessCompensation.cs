using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Flow.Skeleton.Entities;

/// <summary>Persisted compensation binding registered by a process execution.</summary>
[DisplayName("ProcessCompensation")]
[Table("SchemataProcessCompensations")]
[PrimaryKey(nameof(Uid))]
[CanonicalName("process-compensations/{process_compensation}")]
public class SchemataProcessCompensation : IIdentifier, ICanonicalName, ITimestamp
{
    /// <summary>Canonical name of the process that owns this compensation binding.</summary>
    public virtual string Process { get; set; } = null!;

    /// <summary>Canonical name of the scope that registered the compensating activity.</summary>
    public virtual string ScopeOwnerCanonicalName { get; set; } = null!;

    /// <summary>Name of the activity invoked when this binding is compensated.</summary>
    public virtual string ActivityName { get; set; } = null!;

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
