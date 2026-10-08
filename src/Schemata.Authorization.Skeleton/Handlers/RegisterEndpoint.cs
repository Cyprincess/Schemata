using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Handlers;

/// <summary>
///     Abstract handler for the dynamic client registration endpoint, per
///     <seealso href="https://openid.net/specs/openid-connect-registration-1_0.html">OpenID Connect Dynamic Client Registration 1.0</seealso>
///     .
/// </summary>
public abstract class RegisterEndpoint
{
    /// <summary>Processes a registration request and creates a new client.</summary>
    public abstract Task<RegistrationResponse> HandleAsync(RegisterRequest request, string? bearerToken, CancellationToken ct);

    /// <summary>
    ///     Reads back a registered client's metadata with its registration access token, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7592.html#section-2.1">
    ///         RFC 7592: OAuth 2.0 Dynamic Client
    ///         Registration Management Protocol §2.1: Client Read Request
    ///     </seealso>
    ///     .
    /// </summary>
    public abstract Task<RegistrationResponse?> ReadAsync(string? clientId, string? bearerToken, CancellationToken ct);

    /// <summary>
    ///     Replaces a registered client's writable metadata, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7592.html#section-3">
    ///         RFC 7592: OAuth 2.0 Dynamic Client
    ///         Registration Management Protocol §3: Client Update Request
    ///     </seealso>
    ///     . Replace semantics: omitted writable fields are cleared, <c>client_id</c> must match
    ///     and stay immutable, and the four server-managed fields must not be submitted.
    /// </summary>
    public abstract Task<RegistrationResponse?> ReplaceAsync(string? clientId, RegisterRequest request, string? bearerToken, CancellationToken ct);

    /// <summary>
    ///     Deletes a registered client, atomically invalidating its identifier, secret, and
    ///     registration access token, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7592.html#section-3.3">
    ///         RFC 7592: OAuth 2.0 Dynamic Client
    ///         Registration Management Protocol §2.3: Client Delete Request
    ///     </seealso>
    ///     .
    /// </summary>
    public abstract Task<bool> DeleteAsync(string? clientId, string? bearerToken, CancellationToken ct);
}
