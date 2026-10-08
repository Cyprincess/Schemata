using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

/// <summary>
///     Login request body.
/// </summary>
public class LoginRequest
{
    /// <summary>Account username used for authentication.</summary>
    [Required]
    public string Username { get; set; } = null!;

    /// <summary>Account password.</summary>
    [Required]
    public string Password { get; set; } = null!;

    /// <summary>TOTP code from an authenticator app for two-factor verification.</summary>
    public string? TwoFactorCode { get; set; }

    /// <summary>One-time recovery code used when the authenticator app is unavailable.</summary>
    public string? TwoFactorRecoveryCode { get; set; }

    /// <summary>
    ///     Requested Authentication Context Class References, space-separated in order of
    ///     preference, per
    ///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#AuthRequest">
    ///         OpenID Connect Core 1.0 §3.1.2.1: Authentication Request
    ///     </seealso>
    ///     . The login stamps the <c>acr</c> claim with the requested class the authentication
    ///     satisfied; a request it cannot satisfy keeps the performed class (§5.5.1.1).
    /// </summary>
    public string? AcrValues { get; set; }

    /// <summary>Requests cookie-based sign-in.</summary>
    public bool? UseCookies { get; set; }
}
