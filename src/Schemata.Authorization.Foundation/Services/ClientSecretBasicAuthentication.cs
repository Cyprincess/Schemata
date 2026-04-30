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
using static Schemata.Abstractions.SchemataConstants;

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
    IOptions<SchemataAuthorizationOptions> options
) : IClientAuthentication<TApp>
    where TApp : SchemataApplication
{
    #region IClientAuthentication<TApp> Members

    /// <summary>
    ///     Authenticates the client using HTTP Basic.  Returns <c>null</c> when
    ///     Basic auth is not configured as an allowed method, skipping to the
    ///     next authenticator in the chain.
    /// </summary>
    /// <inheritdoc cref="IClientAuthentication{TApp}.AuthenticateAsync" />
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
                SchemataResources.GetResourceString(SchemataResources.ST4001)
            );
        }

        var colon = decoded.IndexOf(':');
        if (colon < 0) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.GetResourceString(SchemataResources.ST4001)
            );
        }

        var id     = WebUtility.UrlDecode(decoded[..colon]);
        var secret = WebUtility.UrlDecode(decoded[(colon + 1)..]);

        if (string.IsNullOrWhiteSpace(id)) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), Parameters.ClientId)
            );
        }

        var app = await apps.FindByClientIdAsync(id, ct);
        if (app == null) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.GetResourceString(SchemataResources.ST4001)
            );
        }

        // Confidential clients MUST present a valid secret (RFC 6749 §2.3.1);
        // public clients may authenticate without one.
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
