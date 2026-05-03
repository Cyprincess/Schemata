using System.Collections.Generic;

namespace Schemata.Authorization.Skeleton.Models;

/// <summary>
///     OpenID Connect Discovery document,
///     per
///     <seealso href="https://openid.net/specs/openid-connect-discovery-1_0.html">OpenID Connect Discovery 1.0</seealso>.
/// </summary>
public sealed class DiscoveryDocument
{
    /// <summary>Issuer identifier URL, used as the <c>"iss"</c> claim value.</summary>
    public string? Issuer { get; set; }

    /// <summary>
    ///     URL of the authorization endpoint.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-3.1">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §3.1: Authorization Endpoint
    ///     </seealso>
    /// </summary>
    public string? AuthorizationEndpoint { get; set; }

    /// <summary>
    ///     URL of the token endpoint.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-3.2">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §3.2: Token Endpoint
    ///     </seealso>
    /// </summary>
    public string? TokenEndpoint { get; set; }

    /// <summary>URL of the JSON Web Key Set document containing the server's signing keys.</summary>
    public string? JwksUri { get; set; }

    /// <summary>
    ///     URL of the UserInfo endpoint.
    ///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#UserInfo">
    ///         OpenID Connect Core 1.0 §5.3:
    ///         UserInfo Endpoint
    ///     </seealso>
    /// </summary>
    public string? UserinfoEndpoint { get; set; }

    /// <summary>
    ///     URL of the device authorization endpoint.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-3.1">
    ///         RFC 8628: OAuth 2.0 Device Authorization
    ///         Grant §3.1: Device Authorization Request
    ///     </seealso>
    /// </summary>
    public string? DeviceAuthorizationEndpoint { get; set; }

    /// <summary>URL of the end session endpoint for RP-Initiated Logout.</summary>
    public string? EndSessionEndpoint { get; set; }

    /// <summary>
    ///     URL of the token introspection endpoint.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7662.html">RFC 7662: OAuth 2.0 Token Introspection</seealso>
    /// </summary>
    public string? IntrospectionEndpoint { get; set; }

    /// <summary>
    ///     URL of the token revocation endpoint.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7009.html">RFC 7009: OAuth 2.0 Token Revocation</seealso>
    /// </summary>
    public string? RevocationEndpoint { get; set; }

    /// <summary>Scope values the server supports.</summary>
    public List<string>? ScopesSupported { get; set; }

    /// <summary>OAuth 2.0 <c>response_type</c> values the server supports.</summary>
    public List<string>? ResponseTypesSupported { get; set; }

    /// <summary>OAuth 2.0 <c>response_mode</c> values the server supports.</summary>
    public List<string>? ResponseModesSupported { get; set; }

    /// <summary>OAuth 2.0 <c>grant_type</c> values the server supports.</summary>
    public List<string>? GrantTypesSupported { get; set; } = [];

    /// <summary>Subject identifier types the server supports, e.g. <c>"public"</c> or <c>"pairwise"</c>.</summary>
    public List<string>? SubjectTypesSupported { get; set; }

    /// <summary>JWS signing algorithms supported for <c>id_token</c>s.</summary>
    public List<string>? IdTokenSigningAlgValuesSupported { get; set; }

    /// <summary>Claim names the server can supply in <c>id_token</c>s or UserInfo.</summary>
    public List<string>? ClaimsSupported { get; set; }

    /// <summary>Client authentication methods supported at the token endpoint.</summary>
    public List<string>? TokenEndpointAuthMethodsSupported { get; set; }

    /// <summary>
    ///     PKCE code challenge methods the server supports.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7636.html">
    ///         RFC 7636: Proof Key for Code Exchange by OAuth Public
    ///         Clients
    ///     </seealso>
    /// </summary>
    public List<string>? CodeChallengeMethodsSupported { get; set; } = [];

    /// <summary>Whether front-channel logout is supported.</summary>
    public bool? FrontchannelLogoutSupported { get; set; }

    /// <summary>Whether the server includes the <c>sid</c> claim in front-channel logout requests.</summary>
    public bool? FrontchannelLogoutSessionSupported { get; set; }

    /// <summary>Whether back-channel logout is supported.</summary>
    public bool? BackchannelLogoutSupported { get; set; }

    /// <summary>Whether the server includes the <c>sid</c> claim in back-channel logout tokens.</summary>
    public bool? BackchannelLogoutSessionSupported { get; set; }

    /// <summary>
    ///     Whether the authorization server sends the <c>iss</c> parameter in authorization responses.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9207.html">
    ///         RFC 9207: OAuth 2.0 Authorization Server Issuer
    ///         Identification
    ///     </seealso>
    /// </summary>
    public bool? AuthorizationResponseIssParameterSupported { get; set; }
}
