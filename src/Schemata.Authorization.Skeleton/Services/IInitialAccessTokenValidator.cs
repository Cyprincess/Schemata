using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton.Services;

/// <summary>
///     Validates initial access tokens presented to the dynamic registration endpoint, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7591.html#section-3">
///         RFC 7591: OAuth 2.0 Dynamic Client
///         Registration Protocol §3: Termination of the Registration Endpoint
///     </seealso>
///     .
/// </summary>
/// <remarks>
///     The authorization server's tightening policy extension point. Hosts implement this
///     interface to accept token-bearing registration requests; without a registration the
///     endpoint denies every request — anonymous or token-bearing — with 401.
/// </remarks>
public interface IInitialAccessTokenValidator
{
    /// <summary>
    ///     Returns <see langword="true" /> when the presented bearer token authorizes a new
    ///     registration; <see langword="false" /> rejects with <c>invalid_token</c> per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6750.html#section-3">
    ///         RFC 6750: The OAuth 2.0 Authorization
    ///         Framework: Bearer Token Usage §3: The WWW-Authenticate Response Header Field
    ///     </seealso>
    ///     .
    /// </summary>
    Task<bool> ValidateAsync(string? bearerToken, CancellationToken ct = default);
}
