using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

public class RegisterRequest
{
    /// <summary>Desired username for the new account.</summary>
    [Required]
    public string Username { get; set; } = null!;

    /// <summary>Email address for the new account; used for verification and recovery.</summary>
    [EmailAddress]
    public string? EmailAddress { get; set; }

    /// <summary>Phone number for the new account; used for verification and recovery.</summary>
    [Phone]
    public string? PhoneNumber { get; set; }

    /// <summary>Password for the new account; must satisfy configured password policy.</summary>
    [Required]
    public string Password { get; set; } = null!;

    /// <summary>When true, issue a cookie instead of a bearer token after registration.</summary>
    public bool? UseCookies { get; set; }
}
