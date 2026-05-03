using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton.Services;

/// <summary>
///     Orchestrates client authentication by iterating all registered
///     <see cref="IClientAuthentication{TApplication}" /> implementations.
///     Exactly one method must match per request,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-2.3">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §2.3: Client Authentication
///     </seealso>
///     .
/// </summary>
public interface IClientAuthenticationService<TApplication>
{
    /// <summary>Authenticates a client using the first matching registered method.</summary>
    Task<TApplication?> AuthenticateAsync(
        Dictionary<string, List<string?>>? query,
        Dictionary<string, List<string?>>? form,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    );
}
