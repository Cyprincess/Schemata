using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton.Services;

/// <summary>
///     Authenticates a client using a specific method (e.g. <c>client_secret_basic</c>,
///     <c>client_secret_post</c>).
/// </summary>
/// <remarks>
///     Returns the authenticated application when the method matches and succeeds,
///     null when the method does not match this request,
///     or throws <see cref="Schemata.Abstractions.Exceptions.OAuthException" /> when the
///     method matches but authentication fails.
/// </remarks>
public interface IClientAuthentication<TApplication>
{
    /// <summary>Attempts to authenticate a client from the given request parameters.</summary>
    Task<TApplication?> AuthenticateAsync(
        Dictionary<string, List<string?>>? query,
        Dictionary<string, List<string?>>? form,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    );
}
