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

    /// <summary>Space-delimited scopes requested.</summary>
    public string? Scope { get; set; }
}
