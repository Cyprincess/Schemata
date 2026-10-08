using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton.Services;

/// <summary>
///     Orchestrates client authentication by determining the actually presented mechanism, then
///     delegating to that single registered
///     <see cref="IClientAuthentication{TApplication}" /> implementation, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-2.3">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §2.3: Client Authentication
///     </seealso>
///     .
/// </summary>
public interface IClientAuthenticationService<TApplication>
    where TApplication : class
{
    /// <summary>
    ///     Authenticates a client through its actually presented mechanism. Returns the located
    ///     application with the mechanism and whether a credential was verified; throws
    ///     <c>invalid_client</c>/<c>invalid_request</c> per RFC 6749 §5.2 otherwise.
    /// </summary>
    Task<ClientAuthenticationResult<TApplication>?> AuthenticateAsync(
        Dictionary<string, List<string?>>? query,
        Dictionary<string, List<string?>>? form,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct,
        string? endpointAudience = null
    );
}
