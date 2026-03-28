namespace Schemata.Identity.Foundation;

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
}
