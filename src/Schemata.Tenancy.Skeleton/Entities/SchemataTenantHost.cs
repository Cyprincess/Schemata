using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;

namespace Schemata.Tenancy.Skeleton.Entities;

/// <summary>
///     Associates a host name with a tenant for host-based tenant resolution.
/// </summary>
/// <remarks>
///     Replaces the JSON-serialized <c>SchemataTenant.Hosts</c> string with a proper
///     one-to-many association so that host look-ups can be indexed at the database level.
/// </remarks>
[DisplayName("TenantHost")]
[Table("SchemataTenantHosts")]
public class SchemataTenantHost : IIdentifier, ITimestamp
{
    /// <summary>Gets or sets the primary-key identifier of the owning tenant (<see cref="IIdentifier.Uid" />).</summary>
    public virtual Guid SchemataTenantUid { get; set; }

    /// <summary>Gets or sets the host name (case-insensitive match source).</summary>
    public virtual string? Host { get; set; }

    #region IIdentifier Members

    [TableKey]
    public virtual Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
