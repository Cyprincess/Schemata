namespace Schemata.Identity.Skeleton.Models;

/// <summary>
///     Request model for two-factor authenticator enrollment and management operations.
/// </summary>
public class AuthenticatorRequest
{
    /// <summary>
    ///     Gets or sets the TOTP code from the authenticator app.
    /// </summary>
    public virtual string? TwoFactorCode { get; set; }

    /// <summary>
    ///     Gets or sets a recovery code for fallback verification.
    /// </summary>
    public virtual string? TwoFactorRecoveryCode { get; set; }
}
