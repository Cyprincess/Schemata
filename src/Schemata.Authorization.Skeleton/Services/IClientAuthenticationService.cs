using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton.Services;

/// <summary>
///     Orchestrates client authentication by iterating all registered <see cref="IClientAuthentication{TApplication}" />
///     implementations.  Exactly one method must match per request (RFC 6749 §2.3).
///     Throws <see cref="Schemata.Abstractions.Exceptions.OAuthException" /> on failure.
/// </summary>
public interface IClientAuthenticationService<TApplication>
{
    Task<TApplication?> AuthenticateAsync(
        Dictionary<string, List<string?>>? query,
        Dictionary<string, List<string?>>? form,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    );
}
