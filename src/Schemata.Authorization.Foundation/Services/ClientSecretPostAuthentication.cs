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

    /// <summary>
    ///     Authenticates the client using form-posted credentials.  Returns
    ///     <c>null</c> when <c>client_secret_post</c> is not an allowed method.
    /// </summary>
    /// <inheritdoc cref="IClientAuthentication{TApp}.AuthenticateAsync" />
    public async Task<TApp?> AuthenticateAsync(
        Dictionary<string, List<string?>>? query,
        Dictionary<string, List<string?>>? form,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    ) {
        if (!options.Value.AllowedClientAuthMethods.Contains(ClientAuthMethods.ClientSecretPost)) {
            return null;
        }

        if (form is null || !form.TryGetValue(Parameters.ClientId, out var ids) || ids.Count != 1) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), Parameters.ClientId)
            );
        }

        var id = ids.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id)) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), Parameters.ClientId)
            );
        }

        form.TryGetValue(Parameters.ClientSecret, out var secrets);
        var secret = secrets?.FirstOrDefault();

        var app = await apps.FindByClientIdAsync(id, ct);
        if (app == null) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.GetResourceString(SchemataResources.ST4001)
            );
        }

        if (!string.IsNullOrWhiteSpace(secret)) {
            if (!await apps.ValidateClientSecretAsync(app, secret, ct)) {
                throw new OAuthException(
                    OAuthErrors.InvalidClient,
                    SchemataResources.GetResourceString(SchemataResources.ST4001)
                );
            }
        } else if (app.ClientType == ClientTypes.Confidential) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.GetResourceString(SchemataResources.ST4002)
            );
        }

        return app;
    }

    #endregion
}
