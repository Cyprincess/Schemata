namespace Schemata.Identity.Skeleton;

public class SchemataIdentityOptions
{
    public bool AllowRegistration { get; set; } = true;

    public bool AllowAccountConfirmation { get; set; } = true;

    public bool AllowPasswordReset { get; set; } = true;

    public bool AllowPasswordChange { get; set; } = true;

    public bool AllowEmailChange { get; set; } = true;

    public bool AllowPhoneNumberChange { get; set; } = true;

    public bool AllowTwoFactorAuthentication { get; set; } = true;
}
