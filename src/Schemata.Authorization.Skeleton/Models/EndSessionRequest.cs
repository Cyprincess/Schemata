namespace Schemata.Authorization.Skeleton.Models;

/// <summary>
///     RP-Initiated Logout request parameters,
///     per
///     <seealso href="https://openid.net/specs/openid-connect-rpinitiated-1_0.html">OpenID Connect RP-Initiated Logout 1.0</seealso>
///     ,
///     OpenID Connect RP-Initiated Logout 1.0.
/// </summary>
public sealed class EndSessionRequest
{
    /// <summary>Previously issued <c>id_token</c> as a hint about the current session.</summary>
    public string? IdTokenHint { get; set; }

    /// <summary>URI to redirect to after logout.</summary>
    public string? PostLogoutRedirectUri { get; set; }

    /// <summary>Opaque value returned unchanged in the post-logout redirect.</summary>
    public string? State { get; set; }

    /// <summary>OAuth 2.0 client identifier.</summary>
    public string? ClientId { get; set; }
}
