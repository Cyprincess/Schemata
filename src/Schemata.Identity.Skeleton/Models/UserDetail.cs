using System;
using System.Collections.Generic;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Identity.Skeleton.Models;

/// <summary>Detailed user response body.</summary>
public class UserDetail : IIdentifier, ICanonicalName, IDescriptive, ITimestamp, IFreshness
{
    /// <summary>ASP.NET Core Identity login name; distinct from the AIP-122 canonical <see cref="Name" />.</summary>
    public string? UserName { get; set; }

    /// <summary>Primary email address for sign-in and notifications.</summary>
    public string? Email { get; set; }

    /// <summary>Set to <see langword="true" /> once the user has verified ownership of <see cref="Email" />.</summary>
    public bool EmailConfirmed { get; set; }

    /// <summary>Phone number used for SMS-based notifications and second-factor sign-in.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>Set to <see langword="true" /> once the user has verified ownership of <see cref="PhoneNumber" />.</summary>
    public bool PhoneNumberConfirmed { get; set; }

    /// <summary>Requires the user to complete a second factor during sign-in.</summary>
    public bool TwoFactorEnabled { get; set; }

    /// <summary>Subjects this account to the configured lockout policy after repeated failed sign-ins.</summary>
    public bool LockoutEnabled { get; set; }

    /// <summary>UTC instant after which the current lockout expires; <see langword="null" /> when not locked.</summary>
    public DateTimeOffset? LockoutEnd { get; set; }

    /// <summary>Consecutive failed sign-in attempts since the last successful one; lockout fires when this reaches the configured threshold.</summary>
    public int AccessFailedCount { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IDescriptive Members

    public string?                      DisplayName  { get; set; }
    public Dictionary<string, string?>? DisplayNames { get; set; }
    public string?                      Description  { get; set; }
    public Dictionary<string, string?>? Descriptions { get; set; }

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
