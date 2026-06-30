using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Authenticates clients via client credentials in the POST body per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-2.3.1">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §2.3.1: Client Password
///     </seealso>
///     .
///     Reads <c>client_id</c> and <c>client_secret</c> from the form body,
///     looks up the application, and validates the client secret.
/// </summary>
public sealed class ClientSecretPostAuthentication<TApp>(
    IApplicationManager<TApp>              apps,
    IOptions<SchemataAuthorizationOptions> options
) : IClientAuthentication<TApp>
    where TApp : SchemataApplication
{
    #region IClientAuthentication<TApp> Members

    public async Task<TApp?> AuthenticateAsync(
        Dictionary<string, List<string?>>? query,
        Dictionary<string, List<string?>>? form,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    ) {
        if (!options.Value.AllowedClientAuthMethods.Contains(ClientAuthMethods.ClientSecretPost)) {
            return null;
        }

        // client_secret_post requires a form-encoded client_id; a missing client_id leaves the
        // request available for the next authenticator (e.g. HTTP Basic) to claim.
        if (form is null || !form.TryGetValue(Parameters.ClientId, out var ids) || ids.Count == 0) {
            return null;
        }

        if (ids.Count != 1) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_EMPTY), Parameters.ClientId)
            );
        }

        var id = ids.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id)) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_EMPTY), Parameters.ClientId)
            );
        }

        form.TryGetValue(Parameters.ClientSecret, out var secrets);
        var secret = secrets?.FirstOrDefault();

        var app = await apps.FindByClientIdAsync(id, ct);
        if (app is null) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.GetResourceString(SchemataResources.INVALID_CLIENT_CREDENTIALS)
            );
        }

        if (!string.IsNullOrWhiteSpace(secret)) {
            if (!await apps.ValidateClientSecretAsync(app, secret, ct)) {
                throw new OAuthException(
                    OAuthErrors.InvalidClient,
                    SchemataResources.GetResourceString(SchemataResources.INVALID_CLIENT_CREDENTIALS)
                );
            }
        } else if (app.ClientType == ClientTypes.Confidential) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.GetResourceString(SchemataResources.CLIENT_SECRET_REQUIRED)
            );
        }

        return app;
    }

    #endregion
}
