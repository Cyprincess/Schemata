using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

/// <summary>
///     Request model for resetting a password using a previously issued reset code.
/// </summary>
public class ResetRequest
{
    /// <summary>
    ///     Gets or sets the email address associated with the account.
    /// </summary>
    [EmailAddress]
    public virtual string? EmailAddress { get; set; }

    /// <summary>
    ///     Gets or sets the phone number associated with the account.
    /// </summary>
    [Phone]
    public virtual string? PhoneNumber { get; set; }

    /// <summary>
    ///     Gets or sets the password reset code.
    /// </summary>
    [Required]
    public virtual string Code { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the new password.
    /// </summary>
    [Required]
    public virtual string Password { get; set; } = null!;
}
