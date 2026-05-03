using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Client authentication orchestrator.  Iterates all registered
///     <see cref="IClientAuthentication{TApp}" /> implementations and expects
///     exactly one to succeed.  Throws <c>invalid_client</c> when zero or
///     multiple authenticators return a result.
/// </summary>
public sealed class ClientAuthenticationService<TApp>(IEnumerable<IClientAuthentication<TApp>> authenticators) : IClientAuthenticationService<TApp>
    where TApp : SchemataApplication
{
    #region IClientAuthenticationService<TApp> Members

    /// <summary>
    ///     Authenticates the client by trying every registered authenticator,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9700.html#section-2.1.3">
    ///         RFC 9700: The OAuth 2.0 Authorization
    ///         Framework: Best Current Practice §2.1.3
    ///     </seealso>
    ///     .
    ///     Multiple matches indicate ambiguous credentials and are rejected.
    /// </summary>
    /// <param name="query">Query parameters from the HTTP request.</param>
    /// <param name="form">Form body parameters from the HTTP request.</param>
    /// <param name="headers">HTTP request headers.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The authenticated application, or throws.</returns>
    public async Task<TApp?> AuthenticateAsync(
        Dictionary<string, List<string?>>? query,
        Dictionary<string, List<string?>>? form,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    ) {
        var results = new List<TApp>();

        foreach (var authenticator in authenticators) {
            var app = await authenticator.AuthenticateAsync(query, form, headers, ct);
            if (app is not null) {
                results.Add(app);
            }
        }

        return results.Count switch {
            1 => results.FirstOrDefault(),
            > 1 => throw new OAuthException(
                OAuthErrors.InvalidRequest,
                SchemataResources.GetResourceString(SchemataResources.ST4003)
            ),
            var _ => throw new OAuthException(
                OAuthErrors.InvalidClient,
                string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), Parameters.ClientId)
            ),
        };
    }

    #endregion
}
