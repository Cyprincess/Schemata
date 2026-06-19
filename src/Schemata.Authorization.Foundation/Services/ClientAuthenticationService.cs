using System.Collections.Generic;
using System.Linq;
using System.Net;
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
            // RFC 6749 §5.2: a failed client authentication is invalid_client and, because credentials
            // may have been presented via the Authorization header, carries HTTP 401 so the exception
            // filter can attach a WWW-Authenticate challenge.
            var _ => throw new OAuthException(
                OAuthErrors.InvalidClient,
                string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), Parameters.ClientId),
                (int)HttpStatusCode.Unauthorized
            ),
        };
    }

    #endregion
}
