using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

/// <summary>
///     Request model for updating user profile fields such as email, phone, or password.
/// </summary>
public class ProfileRequest
{
    /// <summary>
    ///     Gets or sets the new email address.
    /// </summary>
    [EmailAddress]
    public virtual string? EmailAddress { get; set; }

    /// <summary>
    ///     Gets or sets the new phone number.
    /// </summary>
    [Phone]
    public virtual string? PhoneNumber { get; set; }

    /// <summary>
    ///     Gets or sets the current password, required for password change.
    /// </summary>
    public virtual string? OldPassword { get; set; }

    /// <summary>
    ///     Gets or sets the new password.
    /// </summary>
    public virtual string? NewPassword { get; set; }
}
