namespace Schemata.Authorization.Skeleton.Models;

/// <summary>
///     Parameters sent to the OAuth 2.0 authorization endpoint,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.1">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §4.1.1: Authorization Request
///     </seealso>
///     and
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#AuthRequest">
///         OpenID Connect Core 1.0 §3.1.2.1: Authentication Request
///     </seealso>
///     .
/// </summary>
public class AuthorizeRequest
{
    /// <summary>
    ///     OAuth 2.0 client identifier.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-2.2">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §2.2: Client Identifier
    ///     </seealso>
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    ///     URI the authorization server redirects to after approval.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-3.1.2">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §3.1.2: Redirection Endpoint
    ///     </seealso>
    /// </summary>
    public string? RedirectUri { get; set; }

    /// <summary>
    ///     Space-delimited response types, e.g. <c>"code"</c> or <c>"code id_token"</c>.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-3.1.1">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §3.1.1: Response Type
    ///     </seealso>
    /// </summary>
    public string? ResponseType { get; set; }

    /// <summary>
    ///     Space-delimited scopes requested.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-3.3">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §3.3: Access Token Scope
    ///     </seealso>
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    ///     Opaque value for CSRF protection, returned unchanged in the redirect.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.1">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §4.1.1: Authorization Request
    ///     </seealso>
    /// </summary>
    public string? State { get; set; }

    /// <summary>
    ///     Replay-protection value included in the <c>id_token</c>.
    ///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#AuthRequest">
    ///         OpenID Connect Core 1.0 §3.1.2.1:
    ///         Authentication Request
    ///     </seealso>
    /// </summary>
    public string? Nonce { get; set; }

    /// <summary>
    ///     PKCE code challenge.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7636.html#section-4.2">
    ///         RFC 7636: Proof Key for Code Exchange by
    ///         OAuth Public Clients §4.2: Client Creates the Code Challenge
    ///     </seealso>
    /// </summary>
    public string? CodeChallenge { get; set; }

    /// <summary>
    ///     PKCE transformation method: <c>"plain"</c> or <c>"S256"</c>.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7636.html#section-4.3">
    ///         RFC 7636: Proof Key for Code Exchange by
    ///         OAuth Public Clients §4.3: Client Sends the Code Challenge with the Authorization Request
    ///     </seealso>
    /// </summary>
    public string? CodeChallengeMethod { get; set; }

    /// <summary>Mechanism for returning parameters, e.g. <c>"query"</c>, <c>"fragment"</c>, <c>"form_post"</c>.</summary>
    public string? ResponseMode { get; set; }

    /// <summary>
    ///     Space-delimited prompt values controlling authentication UX.
    ///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#AuthRequest">
    ///         OpenID Connect Core 1.0 §3.1.2.1:
    ///         Authentication Request
    ///     </seealso>
    /// </summary>
    public string? Prompt { get; set; }

    /// <summary>Requested display mode for the authorization UI.</summary>
    public string? Display { get; set; }

    /// <summary>Hint to the authorization server about the end-user's login identifier.</summary>
    public string? LoginHint { get; set; }

    /// <summary>Maximum authentication age in seconds; forces re-authentication if exceeded.</summary>
    public string? MaxAge { get; set; }

    /// <summary>Actual authentication time (epoch seconds) recorded when the authorization code was issued.</summary>
    public string? AuthTime { get; set; }

    /// <summary>Previously issued <c>id_token</c> for session validation.</summary>
    public string? IdTokenHint { get; set; }

    /// <summary>
    ///     Space-delimited Authentication Context Class References.
    ///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#AuthRequest">
    ///         OpenID Connect Core 1.0 §3.1.2.1:
    ///         Authentication Request
    ///     </seealso>
    /// </summary>
    public string? AcrValues { get; set; }
}
