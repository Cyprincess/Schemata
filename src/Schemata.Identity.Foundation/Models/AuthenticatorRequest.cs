namespace Schemata.Identity.Foundation.Models;

public class AuthenticatorRequest
{

    public string? TwoFactorCode { get; init; }

    public string? TwoFactorRecoveryCode { get; init; }
}
