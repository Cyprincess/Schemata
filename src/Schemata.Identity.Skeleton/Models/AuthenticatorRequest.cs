namespace Schemata.Identity.Skeleton.Models;

public class AuthenticatorRequest
{
    /// <summary>TOTP code from an authenticator app for two-factor verification.</summary>
    public string? TwoFactorCode { get; set; }

    /// <summary>One-time recovery code used when the authenticator app is unavailable.</summary>
    public string? TwoFactorRecoveryCode { get; set; }
}
