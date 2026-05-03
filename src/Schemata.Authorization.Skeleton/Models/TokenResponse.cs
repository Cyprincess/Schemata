namespace Schemata.Authorization.Skeleton.Models;

/// <summary>
///     OAuth 2.0 token endpoint response,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-5.1">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §5.1: Successful Response
///     </seealso>
///     and
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#TokenResponse">
///         OpenID Connect Core 1.0
///         §3.1.3.3: Successful Token Response
///     </seealso>
///     .
/// </summary>
public sealed class TokenResponse
{
    /// <summary>Issued access token.</summary>
    public string? AccessToken { get; set; }

    /// <summary>
    ///     Token type, typically <c>"Bearer"</c>.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-7.1">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §7.1: Access Token Types
    ///     </seealso>
    /// </summary>
    public string? TokenType { get; set; }

    /// <summary>Lifetime in seconds of the access token.</summary>
    public int ExpiresIn { get; set; }

    /// <summary>Refresh token for obtaining new access tokens.</summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    ///     ID token issued for OpenID Connect authentication.
    ///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#TokenResponse">
    ///         OpenID Connect Core 1.0
    ///         §3.1.3.3: Successful Token Response
    ///     </seealso>
    /// </summary>
    public string? IdToken { get; set; }

    /// <summary>Space-delimited scopes associated with the issued token; may differ from the request.</summary>
    public string? Scope { get; set; }

    /// <summary>Authorization code returned in hybrid flow responses.</summary>
    public string? Code { get; set; }

    /// <summary>
    ///     Issued token type for token exchange.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8693.html#section-2.2.1">
    ///         RFC 8693: OAuth 2.0 Token Exchange
    ///         §2.2.1: Successful Response
    ///     </seealso>
    /// </summary>
    public string? IssuedTokenType { get; set; }
}
