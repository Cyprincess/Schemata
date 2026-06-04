using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Identity.Skeleton.Models;

/// <summary>List item for <see cref="Entities.SchemataUser" />.</summary>
public class UserSummary : IIdentifier, ICanonicalName, ITimestamp
{
    public string? UserName { get; set; }

    public string? Email { get; set; }

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
