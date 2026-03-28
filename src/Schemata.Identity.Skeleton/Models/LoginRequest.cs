using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

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

    /// <summary>When true, issue a cookie instead of a bearer token.</summary>
    public bool? UseCookies { get; set; }
}
