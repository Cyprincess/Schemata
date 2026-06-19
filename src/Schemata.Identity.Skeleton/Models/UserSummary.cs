using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Identity.Skeleton.Models;

/// <summary>User summary response body.</summary>
public class UserSummary : IIdentifier, ICanonicalName, ITimestamp
{
    /// <summary>ASP.NET Core Identity login name; distinct from the AIP-122 canonical <see cref="Name" />.</summary>
    public string? UserName { get; set; }

    /// <summary>Primary email address for sign-in and notifications.</summary>
    public string? Email { get; set; }

    /// <summary>Friendly label shown in user lists; falls back to <see cref="UserName" /> when blank.</summary>
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
