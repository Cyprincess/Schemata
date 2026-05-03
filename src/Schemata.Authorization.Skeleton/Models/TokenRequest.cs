namespace Schemata.Authorization.Skeleton.Models;

/// <summary>
///     Parameters sent to the OAuth 2.0 token endpoint,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.3">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §4.1.3: Access Token Request
///     </seealso>
///     .
/// </summary>
public class TokenRequest
{
    /// <summary>
    ///     Grant type identifier, e.g. <c>"authorization_code"</c> or <c>"refresh_token"</c>.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.3">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §4.1.3: Access Token Request
    ///     </seealso>
    /// </summary>
    public string? GrantType { get; set; }

    /// <summary>
    ///     OAuth 2.0 client identifier.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-2.2">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §2.2: Client Identifier
    ///     </seealso>
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    ///     Client secret for confidential clients.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-2.3.1">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §2.3.1: Client Password
    ///     </seealso>
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    ///     Authorization code received from the authorization endpoint.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.3">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §4.1.3: Access Token Request
    ///     </seealso>
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    ///     PKCE code verifier matching the code challenge sent in the authorize request.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7636.html#section-4.5">
    ///         RFC 7636: Proof Key for Code Exchange by
    ///         OAuth Public Clients §4.5: Client Sends the Authorization Code and the Code Verifier to the Token Endpoint
    ///     </seealso>
    /// </summary>
    public string? CodeVerifier { get; set; }

    /// <summary>
    ///     Redirect URI that was used in the original authorization request.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.3">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §4.1.3: Access Token Request
    ///     </seealso>
    /// </summary>
    public string? RedirectUri { get; set; }

    /// <summary>
    ///     Space-delimited scopes requested; semantics vary by grant type.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-3.3">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §3.3: Access Token Scope
    ///     </seealso>
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    ///     Refresh token for the <c>refresh_token</c> grant.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-6">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §6: Refreshing an Access Token
    ///     </seealso>
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    ///     Device code for the device authorization grant.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-3.4">
    ///         RFC 8628: OAuth 2.0 Device Authorization
    ///         Grant §3.4: Device Access Token Request
    ///     </seealso>
    /// </summary>
    public string? DeviceCode { get; set; }

    /// <summary>
    ///     Security token representing the subject of the exchange.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8693.html#section-2.1">
    ///         RFC 8693: OAuth 2.0 Token Exchange §2.1:
    ///         Request
    ///     </seealso>
    /// </summary>
    public string? SubjectToken { get; set; }

    /// <summary>
    ///     URI identifying the type of the subject token.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8693.html#section-2.1">
    ///         RFC 8693: OAuth 2.0 Token Exchange §2.1:
    ///         Request
    ///     </seealso>
    /// </summary>
    public string? SubjectTokenType { get; set; }

    /// <summary>
    ///     Security token representing the actor in delegation scenarios.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8693.html#section-2.1">
    ///         RFC 8693: OAuth 2.0 Token Exchange §2.1:
    ///         Request
    ///     </seealso>
    /// </summary>
    public string? ActorToken { get; set; }

    /// <summary>
    ///     URI identifying the type of the actor token.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8693.html#section-2.1">
    ///         RFC 8693: OAuth 2.0 Token Exchange §2.1:
    ///         Request
    ///     </seealso>
    /// </summary>
    public string? ActorTokenType { get; set; }
}
