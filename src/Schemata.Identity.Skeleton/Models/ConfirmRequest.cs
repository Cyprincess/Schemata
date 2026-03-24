using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

/// <summary>
///     Request model for confirming an email address or phone number with a verification code.
/// </summary>
public class ConfirmRequest
{
    /// <summary>
    ///     Gets or sets the email address to confirm.
    /// </summary>
    [EmailAddress]
    public virtual string? EmailAddress { get; set; }

    /// <summary>
    ///     Gets or sets the phone number to confirm.
    /// </summary>
    [Phone]
    public virtual string? PhoneNumber { get; set; }

    /// <summary>
    ///     Gets or sets the verification code.
    /// </summary>
    [Required]
    public virtual string Code { get; set; } = null!;
}
