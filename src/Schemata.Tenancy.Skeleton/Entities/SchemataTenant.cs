using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;

namespace Schemata.Tenancy.Skeleton.Entities;

/// <summary>
///     Represents a tenant in a multi-tenant system.
/// </summary>
[DisplayName("Tenant")]
[Table("SchemataTenants")]
[CanonicalName("tenants/{tenant}")]
public class SchemataTenant : IIdentifier, ICanonicalName, IDescriptive, IConcurrency, ITimestamp
{
    /// <summary>Gets or sets the host names associated with this tenant.</summary>
    public virtual ICollection<SchemataTenantHost>? Hosts { get; set; }

    #region ICanonicalName Members

    /// <inheritdoc />
    public virtual string? Name { get; set; }

    /// <inheritdoc />
    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    /// <inheritdoc />
    public virtual Guid? Timestamp { get; set; }

    #endregion

    #region IDescriptive Members

    /// <inheritdoc />
    public virtual string? DisplayName { get; set; }

    /// <inheritdoc />
    public virtual Dictionary<string, string>? DisplayNames { get; set; }

    /// <inheritdoc />
    public virtual string? Description { get; set; }

    /// <inheritdoc />
    public virtual Dictionary<string, string>? Descriptions { get; set; }

    #endregion

    #region IIdentifier Members

    /// <inheritdoc />
    [TableKey]
    public virtual Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    /// <inheritdoc />
    public virtual DateTime? CreateTime { get; set; }

    /// <inheritdoc />
    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
