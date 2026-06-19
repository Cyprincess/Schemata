using System;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Tenancy.Skeleton.Models;

/// <summary>Detailed response body for <see cref="Entities.SchemataTenantHost" />.</summary>
public class TenantHostDetail : IIdentifier, ICanonicalName, ITimestamp, IFreshness
{
    /// <summary>HTTP Host header value that routes requests to the parent tenant.</summary>
    public string? Host { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IFreshness Members

    public string? EntityTag { get; set; }

    #endregion

    #region IIdentifier Members

    public Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    public DateTime? CreateTime { get; set; }
    public DateTime? UpdateTime { get; set; }

    #endregion
}
