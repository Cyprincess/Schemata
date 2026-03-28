using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

public class ProfileRequest
{
    /// <summary>New email address to change to; requires confirmation.</summary>
    [EmailAddress]
    public string? EmailAddress { get; set; }

    /// <summary>New phone number to change to; requires confirmation.</summary>
    [Phone]
    public string? PhoneNumber { get; set; }

    /// <summary>Current password, required when changing password.</summary>
    public string? OldPassword { get; set; }

    /// <summary>Desired new password; must satisfy configured password policy.</summary>
    public string? NewPassword { get; set; }
}
