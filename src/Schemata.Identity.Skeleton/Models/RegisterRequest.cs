using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

/// <summary>
///     Registration request body.
/// </summary>
public class RegisterRequest
{
    /// <summary>Desired username for the new account.</summary>
    [Required]
    public string Username { get; set; } = null!; // Wire DTO: the deserializer assigns the value before use.

    /// <summary>Email address for the new account; used for verification and recovery.</summary>
    [EmailAddress]
    public string? EmailAddress { get; set; }

    /// <summary>Phone number for the new account; used for verification and recovery.</summary>
    [Phone]
    public string? PhoneNumber { get; set; }

    /// <summary>Password for the new account; must satisfy configured password policy.</summary>
    [Required]
    public string Password { get; set; } = null!; // Wire DTO: the deserializer assigns the value before use.

    /// <summary>Requests cookie-based sign-in after registration.</summary>
    public bool? UseCookies { get; set; }
}
