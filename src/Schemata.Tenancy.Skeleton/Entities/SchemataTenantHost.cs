using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

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
    /// <summary>Gets or sets the primary-key identifier of the owning tenant (<see cref="IIdentifier.Id" />).</summary>
    public virtual long SchemataTenantId { get; set; }

    /// <summary>Gets or sets the host name (case-insensitive match source).</summary>
    public virtual string? Host { get; set; }

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
}
