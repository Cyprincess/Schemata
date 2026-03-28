using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton.Services;

/// <summary>
///     Authenticates a client using a specific method (e.g., client_secret_basic, client_secret_post).
///     Returns the authenticated application when the method matches and succeeds,
///     null when the method does not match this request,
///     or throws <see cref="Schemata.Abstractions.Exceptions.OAuthException" /> when the method matches but authentication
///     fails.
/// </summary>
public interface IClientAuthentication<TApplication>
{
    Task<TApplication?> AuthenticateAsync(
        Dictionary<string, List<string?>>? query,
        Dictionary<string, List<string?>>? form,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                 ct
    );
}
