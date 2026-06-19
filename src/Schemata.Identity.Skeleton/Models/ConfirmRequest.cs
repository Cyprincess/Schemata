using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

/// <summary>
///     Email or phone confirmation request body.
/// </summary>
public class ConfirmRequest
{
    /// <summary>Email address to confirm.</summary>
    [EmailAddress]
    public string? EmailAddress { get; set; }

    /// <summary>Phone number to confirm.</summary>
    [Phone]
    public string? PhoneNumber { get; set; }

    /// <summary>Verification code sent to the email or phone.</summary>
    [Required]
    public string Code { get; set; } = null!;
}
