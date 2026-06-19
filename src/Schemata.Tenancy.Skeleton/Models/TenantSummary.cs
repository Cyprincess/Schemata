using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Tenancy.Skeleton.Models;

/// <summary>List item for <see cref="Entities.SchemataTenant" />.</summary>
public class TenantSummary : IIdentifier, ICanonicalName, ITimestamp
{
    /// <summary>Friendly label shown in tenant admin lists.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Free-form long-form description of the tenant.</summary>
    public string? Description { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IIdentifier Members

    public Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    public DateTime? CreateTime { get; set; }
    public DateTime? UpdateTime { get; set; }

    #endregion
}
