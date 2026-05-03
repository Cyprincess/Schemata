namespace Schemata.Authorization.Skeleton.Models;

/// <summary>
///     Token introspection request,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7662.html#section-2.1">
///         RFC 7662: OAuth 2.0 Token Introspection
///         §2.1: Introspection Request
///     </seealso>
///     .
/// </summary>
public class IntrospectRequest
{
    /// <summary>Token value to introspect.</summary>
    public string? Token { get; set; }

    /// <summary>Hint about the type of token, e.g. <c>"access_token"</c> or <c>"refresh_token"</c>.</summary>
    public string? TokenTypeHint { get; set; }

    /// <summary>
    ///     Client identifier for authentication when using the request body.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-2.3.1">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §2.3.1: Client Password
    ///     </seealso>
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>Client secret for authentication when using the request body.</summary>
    public string? ClientSecret { get; set; }
}
