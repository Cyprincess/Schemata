namespace Schemata.Identity.Skeleton.Models;

public class AuthenticatorRequest
{
    public virtual string? TwoFactorCode { get; set; }

    public virtual string? TwoFactorRecoveryCode { get; set; }
}
