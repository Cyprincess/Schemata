using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Tenancy.Skeleton.Entities;

/// <summary>
///     Represents a tenant in a multi-tenant system.
/// </summary>
/// <typeparam name="TKey">The type of the tenant identifier (e.g. <see cref="Guid" />, <see cref="long" />).</typeparam>
/// <remarks>
///     Each tenant has a unique <see cref="TenantId" /> and an optional set of host names
///     used for host-based tenant resolution.
/// </remarks>
[DisplayName("Tenant")]
[Table("SchemataTenants")]
[CanonicalName("tenants/{tenant}")]
public class SchemataTenant<TKey> : IIdentifier, ICanonicalName, IDescriptive, IConcurrency, ITimestamp
    where TKey : struct, IEquatable<TKey>
{
    /// <summary>Gets or sets the tenant-specific identifier used for resolution.</summary>
    public virtual TKey? TenantId { get; set; }

    /// <summary>Gets or sets the JSON-serialized host names associated with this tenant.</summary>
    public virtual string? Hosts { get; set; }

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
    public virtual long Id { get; set; }

    #endregion

    #region ITimestamp Members

    /// <inheritdoc />
    public virtual DateTime? CreateTime { get; set; }

    /// <inheritdoc />
    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
