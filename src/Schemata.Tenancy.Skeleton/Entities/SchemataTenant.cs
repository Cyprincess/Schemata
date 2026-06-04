using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;

namespace Schemata.Tenancy.Skeleton.Entities;

/// <summary>
///     Represents a tenant in a multi-tenant system.
/// </summary>
[DisplayName("Tenant")]
[Table("SchemataTenants")]
[CanonicalName("tenants/{tenant}")]
[PrimaryKey(nameof(Uid))]
public class SchemataTenant : IIdentifier, ICanonicalName, IDescriptive, IConcurrency, ITimestamp
{
    public virtual ICollection<SchemataTenantHost>? Hosts { get; set; }

    #region ICanonicalName Members

    public virtual string? Name { get; set; }

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    public virtual Guid? Timestamp { get; set; }

    #endregion

    #region IDescriptive Members

    public virtual string? DisplayName { get; set; }

    public virtual Dictionary<string, string>? DisplayNames { get; set; }

    public virtual string? Description { get; set; }

    public virtual Dictionary<string, string>? Descriptions { get; set; }

    #endregion

    #region IIdentifier Members
    public virtual Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
