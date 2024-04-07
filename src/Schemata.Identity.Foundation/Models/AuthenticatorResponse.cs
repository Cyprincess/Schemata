namespace Schemata.Identity.Foundation.Models;

public class AuthenticatorResponse
{
    public bool IsTwoFactorEnabled { get; set; }

    public bool IsMachineRemembered { get; set; }

    public int RecoveryCodesLeft { get; set; }

    public string? SharedKey { get; set; }

    public string[]? RecoveryCodes { get; set; }
}
