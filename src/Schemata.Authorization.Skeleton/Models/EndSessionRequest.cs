namespace Schemata.Authorization.Skeleton.Models;

/// <summary>
///     OIDC RP-Initiated Logout 1.0 request parameters.
///     See https://openid.net/specs/openid-connect-rpinitiated-1_0.html
/// </summary>
public sealed class EndSessionRequest
{
    public string? IdTokenHint           { get; set; }
    public string? PostLogoutRedirectUri { get; set; }
    public string? State                 { get; set; }
    public string? ClientId              { get; set; }
}
