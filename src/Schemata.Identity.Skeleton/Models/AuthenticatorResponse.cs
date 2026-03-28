namespace Schemata.Identity.Skeleton.Models;

public class AuthenticatorResponse
{
    /// <summary>Whether two-factor authentication is currently enabled for the user.</summary>
    public bool IsTwoFactorEnabled { get; set; }

    /// <summary>Whether this device has been remembered and can bypass 2FA prompts.</summary>
    public bool IsMachineRemembered { get; set; }

    /// <summary>Number of unused recovery codes remaining.</summary>
    public int RecoveryCodesLeft { get; set; }

    /// <summary>Provided during initial enrollment only.</summary>
    public string? SharedKey { get; set; }

    /// <summary>Provided during initial enrollment only.</summary>
    public string[]? RecoveryCodes { get; set; }
}
