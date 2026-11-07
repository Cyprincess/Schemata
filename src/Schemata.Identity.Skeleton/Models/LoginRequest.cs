using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

/// <summary>
///     Login request body.
/// </summary>
public class LoginRequest
{
    /// <summary>Account username used for authentication when selected by the alias mode.</summary>
    public string? Username { get; set; }

    /// <summary>Email alias used for authentication.</summary>
    [EmailAddress]
    public string? EmailAddress { get; set; }

    /// <summary>Complete E.164 phone alias, or the national number when PhoneRegion is supplied.</summary>
    [Phone]
    public string? PhoneNumber { get; set; }

    /// <summary>Country calling code used to assemble a complete E.164 phone alias.</summary>
    public string? PhoneRegion { get; set; }

    /// <summary>Explicit alias type: user-name, email, or phone-number.</summary>
    public string? UsernameType { get; set; }

    /// <summary>Account password.</summary>
    [Required]
    public string Password { get; set; } = null!; // Wire DTO: the deserializer assigns the value before use.

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
