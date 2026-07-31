namespace Schemata.Identity.Foundation;

/// <summary>Configures Schemata identity endpoints.</summary>
public sealed class SchemataIdentityOptions
{
    /// <summary>Whether the registration endpoint is enabled. Default: true.</summary>
    public bool AllowRegistration { get; set; } = true;

    /// <summary>Whether the email/phone confirmation endpoint is enabled. Default: true.</summary>
    public bool AllowAccountConfirmation { get; set; } = true;

    /// <summary>Whether the forgot-password / reset-password flow is enabled. Default: true.</summary>
    public bool AllowPasswordReset { get; set; } = true;

    /// <summary>Whether authenticated users can change their password. Default: true.</summary>
    public bool AllowPasswordChange { get; set; } = true;

    /// <summary>Whether authenticated users can change their email address. Default: true.</summary>
    public bool AllowEmailChange { get; set; } = true;

    /// <summary>Whether authenticated users can change their phone number. Default: true.</summary>
    public bool AllowPhoneNumberChange { get; set; } = true;

    /// <summary>Whether the 2FA enrollment and downgrade endpoints are enabled. Default: true.</summary>
    public bool AllowTwoFactorAuthentication { get; set; } = true;

    /// <summary>
    ///     Sign-in page a browser hitting an <c>[Authorize]</c> endpoint without a cookie session is
    ///     sent to. The framework appends a <c>continue</c> parameter holding the original local path,
    ///     protected by ASP.NET Data Protection, which <c>GET ~/Authenticate/Continue</c> unprotects
    ///     and redirects back to. Leave unset to answer such requests with 401 instead.
    /// </summary>
    public string? LoginUri { get; set; }
}
