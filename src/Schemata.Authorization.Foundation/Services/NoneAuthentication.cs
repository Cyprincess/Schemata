using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Client authentication method <c>none</c>: the request identifies the client by
///     <c>client_id</c> alone, per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#ClientAuthentication">
///         OpenID Connect Core 1.0 §9: Client Authentication
///     </seealso>
///     . Identification is not proof of confidentiality - a client registered with a credential
///     method must present that credential through its own authenticator and is never claimed here.
/// </summary>
public sealed class NoneAuthentication<TApp>(
    IApplicationManager<TApp>              apps,
    IOptions<SchemataAuthorizationOptions> options
) : IClientAuthentication<TApp>
    where TApp : SchemataApplication
{
    #region IClientAuthentication<TApp> Members

    public string Method => ClientAuthMethods.None;

    public async Task<TApp?> AuthenticateAsync(
        Dictionary<string, List<string?>>? query,
        Dictionary<string, List<string?>>? form,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct,
        string? endpointAudience = null
    ) {
        if (!options.Value.AllowedClientAuthMethods.Contains(ClientAuthMethods.None)) {
            return null;
        }

        // A missing client_id leaves the request available for the next authenticator to claim.
        if (form is null || !form.TryGetValue(Parameters.ClientId, out var ids) || ids.Count == 0) {
            return null;
        }

        if (ids.Count != 1 || string.IsNullOrWhiteSpace(ids[0])) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.NOT_EMPTY, new Dictionary<string, string?> { ["value"] = Parameters.ClientId });
        }

        var app = await apps.FindByClientIdAsync(ids[0], ct);
        if (app is null) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.INVALID_CLIENT_CREDENTIALS);
        }

        // Clients registered with a credential method (or legacy rows without one) authenticate
        // through their own channel; this method never silently downgrades them to none.
        if (app.TokenEndpointAuthMethod != ClientAuthMethods.None) {
            return null;
        }

        return app;
    }

    #endregion
}
