namespace Schemata.Authorization.Skeleton.Models;

/// <summary>
///     Successful Pushed Authorization Request response, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9126.html#section-2.2">
///         RFC 9126: OAuth 2.0 Pushed Authorization Requests §2.2: Pushed Authorization Response
///     </seealso>
///     .
/// </summary>
public sealed class PushedAuthorizationResponse
{
    /// <summary>
    ///     <c>request_uri</c>. The opaque handle the client uses at the authorization endpoint.
    /// </summary>
    public string? RequestUri { get; set; }

    /// <summary>
    ///     <c>expires_in</c>. Lifetime of the pushed request in seconds.
    /// </summary>
    public int ExpiresIn { get; set; }
}