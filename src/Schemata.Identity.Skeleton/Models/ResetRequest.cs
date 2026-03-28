using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

public class ResetRequest
{
    /// <summary>Email address of the account to reset; mutually exclusive with PhoneNumber.</summary>
    [EmailAddress]
    public string? EmailAddress { get; set; }

    /// <summary>Phone number of the account to reset; mutually exclusive with EmailAddress.</summary>
    [Phone]
    public string? PhoneNumber { get; set; }

    /// <summary>Password reset code received via email or SMS.</summary>
    [Required]
    public string Code { get; set; } = null!;

    /// <summary>New password to set; must satisfy configured password policy.</summary>
    [Required]
    public string Password { get; set; } = null!;
}
