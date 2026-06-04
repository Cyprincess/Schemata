using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Tenancy.Skeleton.Models;

/// <summary>List item for <see cref="Entities.SchemataTenantHost" />.</summary>
public class TenantHostSummary : IIdentifier, ICanonicalName, ITimestamp
{
    public string? Host { get; set; }

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
