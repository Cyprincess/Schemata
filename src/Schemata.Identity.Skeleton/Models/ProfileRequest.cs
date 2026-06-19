using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

/// <summary>
///     Profile update request body.
/// </summary>
public class ProfileRequest
{
    /// <summary>Email address requested for the profile.</summary>
    [EmailAddress]
    public string? EmailAddress { get; set; }

    /// <summary>Phone number requested for the profile.</summary>
    [Phone]
    public string? PhoneNumber { get; set; }

    /// <summary>Current password, required when changing password.</summary>
    public string? OldPassword { get; set; }

    /// <summary>Desired new password; must satisfy configured password policy.</summary>
    public string? NewPassword { get; set; }
}
