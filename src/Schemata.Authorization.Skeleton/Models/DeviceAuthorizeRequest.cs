namespace Schemata.Authorization.Skeleton.Models;

/// <summary>
///     Device authorization request,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-3.1">
///         RFC 8628: OAuth 2.0 Device Authorization
///         Grant §3.1: Device Authorization Request
///     </seealso>
///     .
/// </summary>
public class DeviceAuthorizeRequest
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
    ///     Client secret for confidential clients.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-2.3.1">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §2.3.1: Client Password
    ///     </seealso>
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    ///     Client assertion used to authenticate the client itself, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7523.html#section-2.2">
    ///         RFC 7523: JSON Web Token (JWT) Profile for OAuth 2.0
    ///         Client Authentication and Authorization Grants §2.2: Using JWTs for Client Authentication
    ///     </seealso>
    ///     .
    /// </summary>
    public string? ClientAssertion { get; set; }

    /// <summary>
    ///     Type identifier of <see cref="ClientAssertion" />, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7523.html#section-2.2">
    ///         RFC 7523: JSON Web Token (JWT) Profile for OAuth 2.0
    ///         Client Authentication and Authorization Grants §2.2: Using JWTs for Client Authentication
    ///     </seealso>
    ///     .
    /// </summary>
    public string? ClientAssertionType { get; set; }

    /// <summary>Space-delimited scopes requested.</summary>
    public string? Scope { get; set; }
}
