using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

public class ConfirmRequest
{
    /// <summary>Email address to confirm; mutually exclusive with PhoneNumber.</summary>
    [EmailAddress]
    public string? EmailAddress { get; set; }

    /// <summary>Phone number to confirm; mutually exclusive with EmailAddress.</summary>
    [Phone]
    public string? PhoneNumber { get; set; }

    /// <summary>Verification code sent to the email or phone.</summary>
    [Required]
    public string Code { get; set; } = null!;
}
