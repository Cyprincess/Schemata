using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Tenancy.Skeleton.Entities;

[DisplayName("Tenant")]
[Table("SchemataTenants")]
[CanonicalName("tenants/{tenant}")]
public class SchemataTenant<TKey> : IIdentifier, ICanonicalName, IDisplayName, IConcurrency, ITimestamp
    where TKey : struct, IEquatable<TKey>
{
    public virtual TKey? TenantId { get; set; }

    public virtual string? Hosts { get; set; }

    #region ICanonicalName Members

    public virtual string? Name { get; set; }

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    public virtual Guid? Timestamp { get; set; }

    #endregion

    #region IDisplayName Members

    public virtual string? DisplayName { get; set; }

    public virtual string? DisplayNames { get; set; }

    #endregion

    #region IIdentifier Members

    public virtual long Id { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
