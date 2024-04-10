namespace Schemata.Identity.Skeleton.Models;

public class AuthenticatorResponse
{
    public virtual bool IsTwoFactorEnabled { get; set; }

    public virtual bool IsMachineRemembered { get; set; }

    public virtual int RecoveryCodesLeft { get; set; }

    public virtual string? SharedKey { get; set; }

    public virtual string[]? RecoveryCodes { get; set; }
}
