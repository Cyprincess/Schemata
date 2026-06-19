using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Identity.Skeleton.Models;

/// <summary>Role summary response body.</summary>
public class RoleSummary : IIdentifier, ICanonicalName, ITimestamp
{
    /// <summary>Friendly label shown when assigning roles; falls back to <see cref="Name" /> when blank.</summary>
    public string? DisplayName { get; set; }

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
