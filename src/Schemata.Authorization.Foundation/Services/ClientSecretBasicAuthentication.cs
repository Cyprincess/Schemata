using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Authenticates clients via HTTP Basic Authentication per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-2.3.1">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §2.3.1: Client Password
///     </seealso>
///     .
///     Extracts the client ID and secret from the <c>Authorization: Basic</c>
///     header, URL-decodes them, looks up the application, and validates the
///     client secret.  Public clients may omit the secret.
/// </summary>
public sealed class ClientSecretBasicAuthentication<TApp>(
    IApplicationManager<TApp>              apps,
    IOptions<SchemataAuthorizationOptions> options,
    ISecurityStore<SchemataSecurity>       securities,
    ISecretVerifier                        verifier
) : IClientAuthentication<TApp>
    where TApp : SchemataApplication
{
    #region IClientAuthentication<TApp> Members

    public string Method => ClientAuthMethods.ClientSecretBasic;

    public async Task<TApp?> AuthenticateAsync(
        Dictionary<string, List<string?>>? query,
        Dictionary<string, List<string?>>? form,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    ) {
        if (!options.Value.AllowedClientAuthMethods.Contains(ClientAuthMethods.ClientSecretBasic)) {
            return null;
        }

        if (headers is null || !headers.TryGetValue(nameof(Authorization), out var values) || values.Count == 0) {
            return null;
        }

        var header = values.FirstOrDefault(v => v?.StartsWith(Schemes.Basic + " ", StringComparison.OrdinalIgnoreCase) == true);
        if (header is null) {
            return null;
        }

        string decoded;
        try {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[(Schemes.Basic + " ").Length..].Trim()));
        } catch (FormatException) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.GetResourceString(SchemataResources.INVALID_CLIENT_CREDENTIALS)
            );
        }

        var colon = decoded.IndexOf(':');
        if (colon < 0) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.GetResourceString(SchemataResources.INVALID_CLIENT_CREDENTIALS)
            );
        }

        var id     = WebUtility.UrlDecode(decoded[..colon]);
        var secret = WebUtility.UrlDecode(decoded[(colon + 1)..]);

        if (string.IsNullOrWhiteSpace(id)) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_EMPTY), Parameters.ClientId)
            );
        }

        var app = await apps.FindByClientIdAsync(id, ct);
        if (app is null) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.GetResourceString(SchemataResources.INVALID_CLIENT_CREDENTIALS)
            );
        }

        await ClientSecretValidator.ValidateAsync(securities, verifier, app, secret, ct);

        return app;
    }

    #endregion
}
