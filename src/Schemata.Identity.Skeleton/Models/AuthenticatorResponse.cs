namespace Schemata.Identity.Skeleton.Models;

/// <summary>
///     Authenticator enrollment and status response body.
/// </summary>
public class AuthenticatorResponse
{
    /// <summary>Whether two-factor authentication is currently enabled for the user.</summary>
    public bool IsTwoFactorEnabled { get; set; }

    /// <summary>Whether this device has been remembered and can bypass 2FA prompts.</summary>
    public bool IsMachineRemembered { get; set; }

    /// <summary>Number of unused recovery codes remaining.</summary>
    public int RecoveryCodesLeft { get; set; }

    /// <summary>Authenticator shared key returned for enrollment.</summary>
    public string? SharedKey { get; set; }

    /// <summary>Recovery codes returned for enrollment.</summary>
    public string[]? RecoveryCodes { get; set; }
}
