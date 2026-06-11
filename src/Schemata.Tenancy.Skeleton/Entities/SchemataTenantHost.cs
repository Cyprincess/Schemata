using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Entities;

namespace Schemata.Tenancy.Skeleton.Entities;

/// <summary>
///     Associates a host name with a tenant for host-based tenant resolution.
///     Stored as a one-to-many association so host look-ups can be indexed at
///     the database level.
/// </summary>
[DisplayName("Host")]
[Table("SchemataTenantHosts")]
[CanonicalName("tenants/{tenant}/hosts/{host}")]
[PrimaryKey(nameof(Uid))]
public class SchemataTenantHost : IIdentifier, ICanonicalName, ITimestamp
{
    /// <summary>The parent tenant's <see cref="ICanonicalName.Name" />.</summary>
    public virtual string? Tenant { get; set; }

    /// <summary>
    ///     Stored normalized (trimmed, lower-case invariant) for case-insensitive matching.
    /// </summary>
    public virtual string? Host { get; set; }

    #region ICanonicalName Members

    [NotMapped]
    public virtual string? Name
    {
        get => Host;
        set => Host = value;
    }

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
