using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

/// <summary>
///     Request model for username/password authentication, with optional two-factor verification.
/// </summary>
public class LoginRequest
{
    /// <summary>
    ///     Gets or sets the username or email address.
    /// </summary>
    [Required]
    public virtual string Username { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the password.
    /// </summary>
    [Required]
    public virtual string Password { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the TOTP code for two-factor authentication.
    /// </summary>
    public virtual string? TwoFactorCode { get; set; }

    /// <summary>
    ///     Gets or sets a recovery code for two-factor fallback.
    /// </summary>
    public virtual string? TwoFactorRecoveryCode { get; set; }
}
