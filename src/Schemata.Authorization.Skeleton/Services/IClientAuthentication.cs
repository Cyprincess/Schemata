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
    /// <summary>
    ///     Gets the OAuth 2.0 client authentication method identifier served by this
    ///     authenticator (e.g. <c>client_secret_basic</c>), per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-2.3">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §2.3: Client Authentication
    ///     </seealso>
    ///     .
    /// </summary>
    string Method { get; }

    /// <summary>Attempts to authenticate a client from the given request parameters.</summary>
    Task<TApplication?> AuthenticateAsync(
        Dictionary<string, List<string?>>? query,
        Dictionary<string, List<string?>>? form,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    );
}
