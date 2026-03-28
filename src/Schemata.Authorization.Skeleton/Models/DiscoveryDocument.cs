using System.Collections.Generic;

namespace Schemata.Authorization.Skeleton.Models;

/// <summary>
///     OpenID Connect Discovery 1.0 document.
///     See https://openid.net/specs/openid-connect-discovery-1_0.html
/// </summary>
public sealed class DiscoveryDocument
{
    /// <summary>Issuer identifier URL, used as the "iss" claim value.</summary>
    public string? Issuer { get; set; }

    /// <summary>URL of the authorization endpoint per RFC 6749 section 3.1.</summary>
    public string? AuthorizationEndpoint { get; set; }

    /// <summary>URL of the token endpoint per RFC 6749 section 3.2.</summary>
    public string? TokenEndpoint { get; set; }

    /// <summary>URL of the JSON Web Key Set document containing the server's signing keys.</summary>
    public string? JwksUri { get; set; }

    /// <summary>URL of the UserInfo endpoint per OIDC Core section 5.3.</summary>
    public string? UserinfoEndpoint { get; set; }

    /// <summary>URL of the device authorization endpoint per RFC 8628 section 3.1.</summary>
    public string? DeviceAuthorizationEndpoint { get; set; }

    /// <summary>URL of the end session endpoint per OIDC RP-Initiated Logout.</summary>
    public string? EndSessionEndpoint { get; set; }

    /// <summary>URL of the token introspection endpoint per RFC 7662.</summary>
    public string? IntrospectionEndpoint { get; set; }

    /// <summary>URL of the token revocation endpoint per RFC 7009.</summary>
    public string? RevocationEndpoint { get; set; }

    /// <summary>Scope values the server supports.</summary>
    public List<string>? ScopesSupported { get; set; }

    /// <summary>OAuth 2.0 response_type values the server supports.</summary>
    public List<string>? ResponseTypesSupported { get; set; }

    /// <summary>OAuth 2.0 response_mode values the server supports.</summary>
    public List<string>? ResponseModesSupported { get; set; }

    /// <summary>OAuth 2.0 grant_type values the server supports.</summary>
    public List<string>? GrantTypesSupported { get; set; } = [];

    /// <summary>Subject identifier types the server supports, e.g. "public" or "pairwise".</summary>
    public List<string>? SubjectTypesSupported { get; set; }

    /// <summary>JWS signing algorithms supported for id_tokens.</summary>
    public List<string>? IdTokenSigningAlgValuesSupported { get; set; }

    /// <summary>Claim names the server is able to supply in id_tokens or UserInfo.</summary>
    public List<string>? ClaimsSupported { get; set; }

    /// <summary>Client authentication methods supported at the token endpoint.</summary>
    public List<string>? TokenEndpointAuthMethodsSupported { get; set; }

    /// <summary>PKCE code challenge methods the server supports per RFC 7636.</summary>
    public List<string>? CodeChallengeMethodsSupported { get; set; } = [];

    /// <summary>Whether front-channel logout per OIDC Front-Channel Logout is supported.</summary>
    public bool? FrontchannelLogoutSupported { get; set; }

    /// <summary>Whether the server includes the sid claim in front-channel logout requests.</summary>
    public bool? FrontchannelLogoutSessionSupported { get; set; }

    /// <summary>Whether back-channel logout per OIDC Back-Channel Logout is supported.</summary>
    public bool? BackchannelLogoutSupported { get; set; }

    /// <summary>Whether the server includes the sid claim in back-channel logout tokens.</summary>
    public bool? BackchannelLogoutSessionSupported { get; set; }

    /// <summary>RFC 9207 authorization response issuer identifier.</summary>
    public bool? AuthorizationResponseIssParameterSupported { get; set; }
}
