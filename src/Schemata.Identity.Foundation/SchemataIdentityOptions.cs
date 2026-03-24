namespace Schemata.Identity.Foundation;

/// <summary>
///     Configuration options that control which identity endpoints and operations are enabled.
/// </summary>
public sealed class SchemataIdentityOptions
{
    /// <summary>
    ///     Gets or sets a value indicating whether new user registration is allowed. Default is <see langword="true"/>.
    /// </summary>
    public bool AllowRegistration { get; set; } = true;

    /// <summary>
    ///     Gets or sets a value indicating whether email/phone confirmation endpoints are enabled. Default is <see langword="true"/>.
    /// </summary>
    public bool AllowAccountConfirmation { get; set; } = true;

    /// <summary>
    ///     Gets or sets a value indicating whether the forgot/reset password flow is enabled. Default is <see langword="true"/>.
    /// </summary>
    public bool AllowPasswordReset { get; set; } = true;

    /// <summary>
    ///     Gets or sets a value indicating whether authenticated users can change their password. Default is <see langword="true"/>.
    /// </summary>
    public bool AllowPasswordChange { get; set; } = true;

    /// <summary>
    ///     Gets or sets a value indicating whether authenticated users can change their email address. Default is <see langword="true"/>.
    /// </summary>
    public bool AllowEmailChange { get; set; } = true;

    /// <summary>
    ///     Gets or sets a value indicating whether authenticated users can change their phone number. Default is <see langword="true"/>.
    /// </summary>
    public bool AllowPhoneNumberChange { get; set; } = true;

    /// <summary>
    ///     Gets or sets a value indicating whether two-factor authentication management endpoints are enabled. Default is <see langword="true"/>.
    /// </summary>
    public bool AllowTwoFactorAuthentication { get; set; } = true;
}
