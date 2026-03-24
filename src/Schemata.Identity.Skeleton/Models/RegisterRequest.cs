using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

/// <summary>
///     Request model for new user registration.
/// </summary>
public class RegisterRequest
{
    /// <summary>
    ///     Gets or sets the email address for the new account.
    /// </summary>
    [EmailAddress]
    public virtual string? EmailAddress { get; set; }

    /// <summary>
    ///     Gets or sets the phone number for the new account.
    /// </summary>
    [Phone]
    public virtual string? PhoneNumber { get; set; }

    /// <summary>
    ///     Gets or sets the password for the new account.
    /// </summary>
    [Required]
    public virtual string Password { get; set; } = null!;
}
