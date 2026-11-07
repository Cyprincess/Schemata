using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

/// <summary>
///     Registration request body.
/// </summary>
public class RegisterRequest
{
    /// <summary>Desired username; optional when the alias mode selects an email or phone alias.</summary>
    public string? Username { get; set; }

    /// <summary>Explicit alias type: user-name, email, or phone-number.</summary>
    public string? UsernameType { get; set; }

    /// <summary>Email address for the new account; used for verification and recovery.</summary>
    [EmailAddress]
    public string? EmailAddress { get; set; }

    /// <summary>Complete E.164 phone number, or the national number when PhoneRegion is supplied.</summary>
    [Phone]
    public string? PhoneNumber { get; set; }

    /// <summary>Country calling code used to assemble a complete E.164 phone number.</summary>
    public string? PhoneRegion { get; set; }

    /// <summary>Password for the new account; must satisfy configured password policy.</summary>
    [Required]
    public string Password { get; set; } = null!; // Wire DTO: the deserializer assigns the value before use.

    /// <summary>Requests cookie-based sign-in after registration.</summary>
    public bool? UseCookies { get; set; }
}
