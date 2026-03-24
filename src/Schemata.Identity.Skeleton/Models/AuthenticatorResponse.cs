namespace Schemata.Identity.Skeleton.Models;

/// <summary>
///     Response model containing the current state of a user's two-factor authenticator.
/// </summary>
public class AuthenticatorResponse
{
    /// <summary>
    ///     Gets or sets a value indicating whether two-factor authentication is enabled.
    /// </summary>
    public virtual bool IsTwoFactorEnabled { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether this machine is remembered for two-factor bypass.
    /// </summary>
    public virtual bool IsMachineRemembered { get; set; }

    /// <summary>
    ///     Gets or sets the number of unused recovery codes remaining.
    /// </summary>
    public virtual int RecoveryCodesLeft { get; set; }

    /// <summary>
    ///     Gets or sets the shared key for TOTP authenticator setup, provided during initial enrollment.
    /// </summary>
    public virtual string? SharedKey { get; set; }

    /// <summary>
    ///     Gets or sets the generated recovery codes, provided during initial enrollment.
    /// </summary>
    public virtual string[]? RecoveryCodes { get; set; }
}
